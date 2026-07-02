#!/bin/sh
set -eu

log() {
  printf '[app-entrypoint] %s\n' "$*"
}

ROLE_ID_FILE="${ROLE_ID_FILE:-/vault/init/app-role-id}"
SECRET_ID_FILE="${SECRET_ID_FILE:-/vault/init/app-secret-id}"

wait_for_files() {
  attempt=1
  while [ "${attempt}" -le 120 ]; do
    if [ -f "${ROLE_ID_FILE}" ] && [ -f "${SECRET_ID_FILE}" ]; then
      export Vault__RoleId="$(cat "${ROLE_ID_FILE}")"
      export Vault__SecretId="$(cat "${SECRET_ID_FILE}")"
      log "Loaded AppRole credentials from Vault init volume."
      exec dotnet PostgresVaultService.dll
    fi
    log "Waiting for Vault AppRole credentials (${attempt}/120)..."
    sleep 2
    attempt=$((attempt + 1))
  done
  log "AppRole credentials were not found."
  exit 1
}

wait_for_files
