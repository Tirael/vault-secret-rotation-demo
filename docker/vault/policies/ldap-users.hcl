path "auth/ldap/login/*" {
  capabilities = ["create", "read"]
}

path "secret/data/postgresql/*" {
  capabilities = ["read"]
}
