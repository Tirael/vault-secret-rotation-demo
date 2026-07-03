using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;
using PostgresVaultService.Options;
using PostgresVaultService.Resilience;

namespace PostgresVaultService.Services;

public sealed class InsertWorker : BackgroundService
{
    private readonly DynamicPostgresConnectionFactory _connectionFactory;
    private readonly PostgresOptions _options;
    private readonly ResiliencePipeline _postgresPipeline;
    private readonly ILogger<InsertWorker> _logger;

    public InsertWorker(
        DynamicPostgresConnectionFactory connectionFactory,
        IOptions<PostgresOptions> options,
        ResiliencePipelineProvider<string> pipelineProvider,
        ILogger<InsertWorker> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _postgresPipeline = pipelineProvider.GetPipeline(ResiliencePipelineNames.PostgresOperation);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Insert worker started. Interval: {IntervalSeconds}s", _options.InsertIntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.InsertIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _postgresPipeline.ExecuteAsync(
                    async token => await InsertHeartbeatAsync(token).ConfigureAwait(false),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Insert failed after resilience pipeline retries. Retrying on next tick.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task InsertHeartbeatAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO app_events (event_type, payload, created_at)
            VALUES (@event_type, @payload::jsonb, NOW())
            RETURNING id;
            """;
        command.Parameters.AddWithValue("event_type", "heartbeat");
        command.Parameters.AddWithValue("payload", $$"""{"source":"PostgresVaultService","machine":"{{Environment.MachineName}}"}""");

        var id = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Inserted heartbeat event with id {EventId}", id);
    }
}
