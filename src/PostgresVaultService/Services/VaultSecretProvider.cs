using Microsoft.Extensions.Options;
using PostgresVaultService.Models;
using PostgresVaultService.Options;
using VaultSharp;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.Commons;

namespace PostgresVaultService.Services;

public sealed class VaultSecretProvider : IVaultSecretProvider
{
    private readonly VaultOptions _options;
    private readonly ILogger<VaultSecretProvider> _logger;
    private readonly SemaphoreSlim _clientLock = new(1, 1);

    private IVaultClient? _client;

    public VaultSecretProvider(IOptions<VaultOptions> options, ILogger<VaultSecretProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PostgresCredentials> GetCredentialsAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var client = await GetAuthenticatedClientAsync().ConfigureAwait(false);
        var secret = await client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(
                NormalizeSecretPath(_options.SecretPath),
                mountPoint: "secret")
            .ConfigureAwait(false);

        var data = secret.Data.Data;
        var username = GetRequiredValue(data, "username");
        var password = GetRequiredValue(data, "password");

        return new PostgresCredentials(username, password);
    }

    private async Task<IVaultClient> GetAuthenticatedClientAsync()
    {
        if (_client is not null)
        {
            return _client;
        }

        await _clientLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var settings = new VaultClientSettings(
                _options.Address,
                new AppRoleAuthMethodInfo(_options.RoleId, _options.SecretId));

            _client = new VaultClient(settings);
            await _client.V1.Auth.PerformImmediateLogin().ConfigureAwait(false);
            _logger.LogInformation("Authenticated to Vault via AppRole");
            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private static string NormalizeSecretPath(string secretPath)
    {
        const string prefix = "secret/data/";
        return secretPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? secretPath[prefix.Length..]
            : secretPath;
    }

    private static string GetRequiredValue(IDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null)
        {
            throw new InvalidOperationException($"Vault secret is missing required field '{key}'.");
        }

        return Convert.ToString(value) ?? throw new InvalidOperationException($"Vault secret field '{key}' is empty.");
    }
}

public interface IVaultSecretProvider
{
    Task<PostgresCredentials> GetCredentialsAsync(CancellationToken cancellationToken);
}
