#!/bin/sh
set -eu

VAULT_ADDR="${VAULT_ADDR:-http://vault:8200}"
VAULT_INIT_FILE="${VAULT_INIT_FILE:-/vault/init/root-token}"
MAX_ATTEMPTS=60
POSTGRES_ADMIN_PASSWORD="${POSTGRES_ADMIN_PASSWORD:-postgres-admin-secret}"
TECH_USER="${TECH_USER:-app_tech}"
STATIC_ROLE="${STATIC_ROLE:-app-tech}"
ROTATION_PERIOD="${ROTATION_PERIOD:-1h}"

log() {
  printf '[vault-init] %s\n' "$*"
}

wait_for_vault() {
  attempt=1
  while [ "${attempt}" -le "${MAX_ATTEMPTS}" ]; do
    if curl -fsS "${VAULT_ADDR}/v1/sys/health?standbyok=true&sealedcode=200&uninitcode=200" >/dev/null 2>&1; then
      return 0
    fi
    log "Waiting for Vault (${attempt}/${MAX_ATTEMPTS})..."
    sleep 2
    attempt=$((attempt + 1))
  done
  log "Vault did not become ready in time."
  return 1
}

init_vault() {
  mkdir -p /vault/init

  if vault status -format=json 2>/dev/null | jq -e '.initialized == true' >/dev/null; then
    log "Vault already initialized."
    if [ -f /vault/init/unseal-key ]; then
      vault operator unseal "$(cat /vault/init/unseal-key)" >/dev/null
    fi
    export VAULT_TOKEN="$(cat "${VAULT_INIT_FILE}")"
    return 0
  fi

  log "Initializing Vault..."
  init_result="$(vault operator init -key-shares=1 -key-threshold=1 -format=json)"
  ROOT_TOKEN="$(echo "${init_result}" | jq -r '.root_token')"
  UNSEAL_KEY="$(echo "${init_result}" | jq -r '.unseal_keys_b64[0]')"
  echo "${ROOT_TOKEN}" > "${VAULT_INIT_FILE}"
  echo "${UNSEAL_KEY}" > /vault/init/unseal-key
  vault operator unseal "${UNSEAL_KEY}" >/dev/null
  export VAULT_TOKEN="${ROOT_TOKEN}"
}

configure_ldap_directory() {
  log "Bootstrapping LDAP users and groups..."
  ldapadd -x -H "ldap://${LDAP_HOST:-ldap}:389" \
    -D "cn=admin,dc=demo,dc=local" \
    -w "${FREEIPA_ADMIN_PASSWORD}" \
    -f /bootstrap/10-users-groups.ldif >/dev/null 2>&1 || true
}

configure_vault() {
  if [ -f /vault/init/configured ]; then
    log "Vault already configured. Skipping."
    return 0
  fi

  log "Configuring Vault engines and auth methods..."

  vault secrets enable -path=secret kv-v2 2>/dev/null || true

  if ! vault secrets list -format=json | jq -e '.["database/"]' >/dev/null; then
    vault secrets enable database
  fi

  vault write database/config/postgresql \
    plugin_name=postgresql-database-plugin \
    allowed_roles="*" \
    verify_connection=true \
    connection_url="postgresql://{{username}}:{{password}}@${PGHOST:-postgres}:5432/postgres?sslmode=disable" \
    username="postgres" \
    password="${POSTGRES_ADMIN_PASSWORD}"

  vault write "database/static-roles/${STATIC_ROLE}" \
    db_name=postgresql \
    username="${TECH_USER}" \
    rotation_period="${ROTATION_PERIOD}" \
    rotation_statements="ALTER USER \"{{name}}\" WITH PASSWORD '{{password}}';"

  log "Triggering initial static role rotation for ${TECH_USER}..."
  vault read -format=json "database/static-creds/${STATIC_ROLE}" >/dev/null

  vault policy write app-service /vault/policies/app-service.hcl
  vault policy write ldap-users /vault/policies/ldap-users.hcl

  if ! vault auth list -format=json | jq -e '.["approle/"]' >/dev/null; then
    vault auth enable approle
  fi

  vault write auth/approle/role/app-service \
    token_policies=app-service \
    token_ttl=1h \
    token_max_ttl=4h \
    secret_id_ttl=0 \
    secret_id_num_uses=0

  ROLE_ID="$(vault read -field=role_id auth/approle/role/app-service/role-id)"
  SECRET_ID="$(vault write -f -field=secret_id auth/approle/role/app-service/secret-id)"

  mkdir -p /vault/init
  echo "${ROLE_ID}" > /vault/init/app-role-id
  echo "${SECRET_ID}" > /vault/init/app-secret-id

  if ! vault auth list -format=json | jq -e '.["ldap/"]' >/dev/null; then
    vault auth enable ldap
  fi

  vault write auth/ldap/config \
    url="ldap://${LDAP_HOST:-ldap}:389" \
    binddn="cn=admin,dc=demo,dc=local" \
    bindpass="${FREEIPA_ADMIN_PASSWORD}" \
    userdn="ou=users,dc=demo,dc=local" \
    groupdn="ou=groups,dc=demo,dc=local" \
    userattr="uid" \
    upndomain="demo.local" \
    insecure_tls=true

  vault write auth/ldap/groups/admins policies=ldap-users
  vault write auth/ldap/groups/ipausers policies=ldap-users

  log "Vault configuration completed."
  log "Database static role: database/static-creds/${STATIC_ROLE}"
  log "AppRole role_id stored in /vault/init/app-role-id"
  touch /vault/init/configured
}

wait_for_ldap() {
  attempt=1
  while [ "${attempt}" -le 60 ]; do
    if ldapsearch -x -H "ldap://${LDAP_HOST:-ldap}:389" -b '' -s base '(objectclass=*)' namingContexts 2>/dev/null | grep -q 'dc=demo,dc=local'; then
      return 0
    fi
    log "Waiting for LDAP (${attempt}/60)..."
    sleep 5
    attempt=$((attempt + 1))
  done
  log "LDAP did not become ready in time."
  return 1
}

main() {
  export VAULT_ADDR="${VAULT_ADDR:-http://vault:8200}"
  wait_for_vault
  init_vault
  wait_for_ldap || log "Continuing without confirmed LDAP readiness."
  configure_ldap_directory
  configure_vault
  log "Init container finished successfully."
}

main "$@"
