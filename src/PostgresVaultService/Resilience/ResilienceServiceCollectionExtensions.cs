using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using PostgresVaultService.Options;

namespace PostgresVaultService.Resilience;

public static class ResilienceServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationResiliencePipelines(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));

        services.AddResiliencePipeline(ResiliencePipelineNames.VaultCredentials, (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

            builder
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = options.VaultMaxRetryAttempts,
                    Delay = TimeSpan.FromSeconds(options.VaultRetryDelaySeconds),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(ResiliencePredicates.IsVaultTransient)
                })
                .AddTimeout(TimeSpan.FromSeconds(options.VaultTimeoutSeconds));
        });

        services.AddResiliencePipeline(ResiliencePipelineNames.PostgresOperation, (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value;

            builder
                .AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = options.PostgresMaxRetryAttempts,
                    Delay = TimeSpan.FromSeconds(options.PostgresRetryDelaySeconds),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = new PredicateBuilder().Handle<Exception>(ResiliencePredicates.IsPostgresTransient)
                })
                .AddTimeout(TimeSpan.FromSeconds(options.PostgresTimeoutSeconds));
        });

        return services;
    }
}
