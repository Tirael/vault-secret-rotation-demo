namespace PostgresVaultService.Options;

public sealed class VaultOptions
{
    public const string SectionName = "Vault";

    public string Address { get; set; } = "http://vault:8200";

    public string RoleId { get; set; } = string.Empty;

    public string SecretId { get; set; } = string.Empty;

    public string DatabaseMountPoint { get; set; } = "database";

    public string StaticRoleName { get; set; } = "app-tech";

    public int PollIntervalSeconds { get; set; } = 15;
}
