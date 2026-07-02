namespace PostgresVaultService.Options;

public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    public string Host { get; set; } = "postgres";

    public int Port { get; set; } = 5432;

    public string Database { get; set; } = "appdb";

    public string Username { get; set; } = "app_tech";

    public int InsertIntervalSeconds { get; set; } = 5;

    public int CommandTimeoutSeconds { get; set; } = 30;
}
