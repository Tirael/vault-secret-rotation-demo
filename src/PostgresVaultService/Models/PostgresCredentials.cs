namespace PostgresVaultService.Models;

public sealed record PostgresCredentials(string Username, string Password)
{
    internal const string MaskedPassword = "***";

    public bool IsEquivalentTo(PostgresCredentials? other) =>
        other is not null &&
        string.Equals(Username, other.Username, StringComparison.Ordinal) &&
        string.Equals(Password, other.Password, StringComparison.Ordinal);

    public override string ToString() =>
        $"{nameof(PostgresCredentials)} {{ {nameof(Username)} = {Username}, {nameof(Password)} = {MaskedPassword} }}";
}
