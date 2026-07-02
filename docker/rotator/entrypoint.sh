#!/bin/sh
set -eu

log() {
  printf '[rotator] %s\n' "$*"
}

if [ -z "${VAULT_TOKEN:-}" ] && [ -f "${VAULT_TOKEN_FILE:-/vault/init/rotator-token}" ]; then
  VAULT_TOKEN="$(cat "${VAULT_TOKEN_FILE}")"
  export VAULT_TOKEN
fi

if [ -z "${VAULT_TOKEN:-}" ]; then
  log "Waiting for rotator Vault token..."
  attempt=1
  while [ "${attempt}" -le 120 ]; do
    if [ -f "${VAULT_TOKEN_FILE:-/vault/init/rotator-token}" ]; then
      VAULT_TOKEN="$(cat "${VAULT_TOKEN_FILE}")"
      export VAULT_TOKEN
      break
    fi
    sleep 2
    attempt=$((attempt + 1))
  done
fi

if [ -z "${VAULT_TOKEN:-}" ]; then
  log "Rotator token was not found."
  exit 1
fi

log "Running initial password rotation"
/usr/local/bin/rotate-password.sh

cat >/etc/crontabs/root <<'CRON'
0 * * * * /usr/local/bin/rotate-password.sh >> /var/log/rotation.log 2>&1
CRON

log "Starting hourly rotation scheduler"
exec crond -f -l 2
