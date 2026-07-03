using Microsoft.Extensions.Options;
using Npgsql;
using Polly;
using PostgresVaultService.Options;
using VaultSharp.Core;

namespace PostgresVaultService.Resilience;

internal static class ResiliencePredicates
{
    public static bool IsVaultTransient(Exception exception) =>
        exception switch
        {
            HttpRequestException => true,
            IOException => true,
            TimeoutException => true,
            VaultApiException vaultApiException when (int)vaultApiException.HttpStatusCode >= 500 => true,
            _ => false
        };

    public static bool IsPostgresTransient(Exception exception) =>
        exception switch
        {
            PostgresException postgresException => postgresException.SqlState switch
            {
                "57P01" or "57P02" or "57P03" or "08000" or "08003" or "08006" or "40001" or "55P03" => true,
                "28P01" or "28000" => false,
                _ => false
            },
            NpgsqlException npgsqlException => npgsqlException.IsTransient,
            TimeoutException => true,
            IOException => true,
            _ => false
        };
}
