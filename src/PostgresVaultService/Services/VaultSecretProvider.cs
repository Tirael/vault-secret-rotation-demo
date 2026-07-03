using Microsoft.Extensions.Options;
using PostgresVaultService.Models;
using PostgresVaultService.Options;
using VaultSharp;
using VaultSharp.V1.AuthMethods.AppRole;

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
        var secret = await client.V1.Secrets.Database
            .GetStaticCredentialsAsync(_options.StaticRoleName, _options.DatabaseMountPoint)
            .ConfigureAwait(false);

        var data = secret.Data
            ?? throw new InvalidOperationException("Vault returned empty static credentials.");

        if (string.IsNullOrWhiteSpace(data.Username) || string.IsNullOrWhiteSpace(data.Password))
        {
            throw new InvalidOperationException("Vault static credentials are missing username or password.");
        }

        return new PostgresCredentials(data.Username, data.Password);
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
}

public interface IVaultSecretProvider
{
    Task<PostgresCredentials> GetCredentialsAsync(CancellationToken cancellationToken);
}
