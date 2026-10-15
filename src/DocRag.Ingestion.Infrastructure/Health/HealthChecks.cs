using DocRag.Ingestion.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Health;

/// <summary>Readiness check running <c>SELECT 1</c> against PostgreSQL.</summary>
public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.", ex);
        }
    }
}

/// <summary>Readiness check verifying the Garage bucket exists and is accessible, without creating it.</summary>
public sealed class GarageHealthCheck(IObjectStorage storage) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.EnsureAccessibleAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The object storage bucket is not accessible.", ex);
        }
    }
}
