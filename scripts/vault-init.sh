#!/bin/sh
set -eu

VAULT_ADDR="${VAULT_ADDR:-http://vault:8200}"
VAULT_INIT_FILE="${VAULT_INIT_FILE:-/vault/init/root-token}"
MAX_ATTEMPTS=60

log() {
  printf '[vault-init] %s\n' "$*"
}

wait_for_vault() {
  attempt=1
  while [ "${attempt}" -le "${MAX_ATTEMPTS}" ]; do
    if curl -fsS "${VAULT_ADDR}/v1/sys/health" >/dev/null 2>&1; then
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

configure_vault() {
  if [ -f /vault/init/configured ]; then
    log "Vault already configured. Skipping."
    return 0
  fi

  log "Configuring Vault engines and auth methods..."

  vault secrets enable -path=secret kv-v2 2>/dev/null || true

  vault policy write app-service /vault/policies/app-service.hcl
  vault policy write rotator /vault/policies/rotator.hcl
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
    url="ldap://freeipa:389" \
    binddn="uid=admin,cn=users,cn=accounts,dc=demo,dc=local" \
    bindpass="${FREEIPA_ADMIN_PASSWORD}" \
    userdn="cn=users,cn=accounts,dc=demo,dc=local" \
    groupdn="cn=groups,cn=accounts,dc=demo,dc=local" \
    userattr="uid" \
    upndomain="demo.local" \
    insecure_tls=true

  vault write auth/ldap/groups/admins policies=ldap-users
  vault write auth/ldap/groups/ipausers policies=ldap-users

  vault write auth/token/roles/rotator \
    allowed_policies=rotator \
    orphan=true \
    period=24h \
    renewable=true

  ROTATOR_TOKEN="$(vault token create -role=rotator -format=json | jq -r '.auth.client_token')"
  echo "${ROTATOR_TOKEN}" > /vault/init/rotator-token

  vault kv put secret/postgresql/app_tech \
    username="${TECH_USER:-app_tech}" \
    password="${INITIAL_TECH_PASSWORD:-ChangeMe_OnFirstRotation!}"

  log "Vault configuration completed."
  log "AppRole role_id stored in /vault/init/app-role-id"
  touch /vault/init/configured
}

wait_for_freeipa() {
  attempt=1
  while [ "${attempt}" -le 120 ]; do
    if ldapsearch -x -H ldap://freeipa:389 -b "dc=demo,dc=local" -s base "(objectclass=*)" >/dev/null 2>&1; then
      return 0
    fi
    log "Waiting for FreeIPA LDAP (${attempt}/120)..."
    sleep 5
    attempt=$((attempt + 1))
  done
  log "FreeIPA LDAP did not become ready in time."
  return 1
}

main() {
  export VAULT_ADDR="${VAULT_ADDR:-http://vault:8200}"
  wait_for_vault
  init_vault
  wait_for_freeipa || log "Continuing without confirmed FreeIPA readiness."
  configure_vault
  log "Init container finished successfully."
}

main "$@"
