namespace PostgresVaultService.Logging;

public static class StructuredLoggingExtensions
{
    public static HostApplicationBuilder AddStructuredLogging(this HostApplicationBuilder builder)
    {
        builder.Services.AddSerilog((services, loggerConfiguration) =>
            loggerConfiguration
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Destructure.ByTransforming<PostgresCredentials>(credentials => new
                {
                    credentials.Username,
                    Password = PostgresCredentials.MaskedPassword
                })
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "PostgresVaultService"));

        return builder;
    }

    public static async Task RunWithStructuredLoggingAsync(this IHost host)
    {
        try
        {
            await host.RunAsync();
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
