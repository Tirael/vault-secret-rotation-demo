namespace PostgresVaultService.Services;

public sealed class DynamicPostgresConnectionFactory : IHostedService, IDisposable
{
    private static readonly TimeSpan OldDataSourceDrainDelay = TimeSpan.FromSeconds(60);

    private readonly IVaultSecretProvider _vaultSecretProvider;
    private readonly PostgresOptions _postgresOptions;
    private readonly VaultOptions _vaultOptions;
    private readonly ResiliencePipeline _postgresPipeline;
    private readonly ILogger<DynamicPostgresConnectionFactory> _logger;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;
    private NpgsqlDataSource? _currentDataSource;
    private PostgresCredentials? _currentCredentials;

    public DynamicPostgresConnectionFactory(
        IVaultSecretProvider vaultSecretProvider,
        IOptions<PostgresOptions> postgresOptions,
        IOptions<VaultOptions> vaultOptions,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<DynamicPostgresConnectionFactory> logger)
    {
        _vaultSecretProvider = vaultSecretProvider;
        _postgresOptions = postgresOptions.Value;
        _vaultOptions = vaultOptions.Value;
        _postgresPipeline = pipelineProvider.GetPipeline(ResiliencePipelineNames.PostgresOperation);
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshIfNeededAsync(force: true, cancellationToken).ConfigureAwait(false);

        _pollCts = new CancellationTokenSource();
        _pollTask = PollSecretsLoopAsync(_pollCts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_pollCts is not null)
        {
            await _pollCts.CancelAsync().ConfigureAwait(false);
        }

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        if (_currentDataSource is not null)
        {
            await _currentDataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _pollCts?.Dispose();
        _refreshLock.Dispose();
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var dataSource = _currentDataSource
                ?? throw new InvalidOperationException("PostgreSQL data source is not initialized.");

            try
            {
                return await _postgresPipeline.ExecuteAsync(
                    async token => await dataSource.OpenConnectionAsync(token).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException ex) when (ex.SqlState is "28P01" or "28000")
            {
                _logger.LogWarning(ex, "PostgreSQL authentication failed on attempt {Attempt}. Refreshing credentials.", attempt);
                await RefreshIfNeededAsync(force: true, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Unable to open PostgreSQL connection after credential refresh attempts.");
    }

    private async Task PollSecretsLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_vaultOptions.PollIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshIfNeededAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vault secret polling loop failed.");
        }
    }

    private async Task RefreshIfNeededAsync(bool force, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credentials = await _vaultSecretProvider.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);

            if (!force && _currentCredentials?.IsEquivalentTo(credentials) == true)
            {
                return;
            }

            var connectionString = BuildConnectionString(credentials);
            var newDataSource = NpgsqlDataSource.Create(connectionString);
            var previousDataSource = _currentDataSource;

            _currentDataSource = newDataSource;
            _currentCredentials = credentials;

            _logger.LogInformation("PostgreSQL credentials refreshed for user {Username}", credentials.Username);

            if (previousDataSource is not null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(OldDataSourceDrainDelay, CancellationToken.None).ConfigureAwait(false);
                        await previousDataSource.DisposeAsync().ConfigureAwait(false);
                        _logger.LogInformation("Disposed previous PostgreSQL data source after drain delay.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to dispose previous PostgreSQL data source.");
                    }
                }, CancellationToken.None);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private string BuildConnectionString(PostgresCredentials credentials)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = _postgresOptions.Host,
            Port = _postgresOptions.Port,
            Database = _postgresOptions.Database,
            Username = credentials.Username,
            Password = credentials.Password,
            Pooling = true,
            MinPoolSize = 1,
            MaxPoolSize = 20,
            Timeout = _postgresOptions.CommandTimeoutSeconds,
            CommandTimeout = _postgresOptions.CommandTimeoutSeconds,
            IncludeErrorDetail = true
        };

        return builder.ConnectionString;
    }
}
