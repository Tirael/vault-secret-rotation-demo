#!/bin/sh
set -eu

log() {
  printf '[%s] %s\n' "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" "$*"
}

VAULT_ADDR="${VAULT_ADDR:-http://vault:8200}"
VAULT_TOKEN="${VAULT_TOKEN:?VAULT_TOKEN is required}"
PGHOST="${PGHOST:-postgres}"
PGPORT="${PGPORT:-5432}"
PGDATABASE="${PGDATABASE:-postgres}"
PGUSER="${PGUSER:-postgres}"
PGPASSWORD="${PGPASSWORD:?PGPASSWORD is required}"
TECH_USER="${TECH_USER:-app_tech}"
SECRET_PATH="${SECRET_PATH:-secret/data/postgresql/app_tech}"

NEW_PASSWORD="$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-32)"
export PGPASSWORD

log "Rotating password for technical user ${TECH_USER}"

psql -h "${PGHOST}" -p "${PGPORT}" -U "${PGUSER}" -d "${PGDATABASE}" -v ON_ERROR_STOP=1 \
  -c "ALTER ROLE ${TECH_USER} WITH PASSWORD '${NEW_PASSWORD}';"

payload="$(jq -n --arg username "${TECH_USER}" --arg password "${NEW_PASSWORD}" '{data:{username:$username,password:$password}}')"

curl -fsS \
  -H "X-Vault-Token: ${VAULT_TOKEN}" \
  -H "Content-Type: application/json" \
  -X POST \
  -d "${payload}" \
  "${VAULT_ADDR}/v1/${SECRET_PATH}" >/dev/null

log "Password rotated and stored in Vault at ${SECRET_PATH}"
