ui = true

listener "tcp" {
  address     = "0.0.0.0:8200"
  tls_disable = 1
}

storage "file" {
  path = "/vault/file"
}

api_addr = "http://vault:8200"
cluster_addr = "https://vault:8201"
disable_mlock = true
