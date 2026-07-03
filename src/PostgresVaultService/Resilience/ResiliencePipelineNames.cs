namespace PostgresVaultService.Resilience;

public static class ResiliencePipelineNames
{
    public const string VaultCredentials = "vault-credentials";
    public const string PostgresOperation = "postgres-operation";
}
