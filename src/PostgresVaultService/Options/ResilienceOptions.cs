namespace PostgresVaultService.Options;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public int VaultMaxRetryAttempts { get; set; } = 3;

    public int VaultRetryDelaySeconds { get; set; } = 2;

    public int VaultTimeoutSeconds { get; set; } = 30;

    public int PostgresMaxRetryAttempts { get; set; } = 3;

    public int PostgresRetryDelaySeconds { get; set; } = 2;

    public int PostgresTimeoutSeconds { get; set; } = 30;
}
