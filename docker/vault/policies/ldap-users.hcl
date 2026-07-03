path "auth/ldap/login/*" {
  capabilities = ["create", "read"]
}

path "database/static-creds/app-tech" {
  capabilities = ["read"]
}
