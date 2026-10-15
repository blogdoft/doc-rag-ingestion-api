using System.Security.Cryptography;
using System.Text;
using BlogDoFT.Libs.WarmUp;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations;

/// <summary>
/// Applies pending migrations as a warm-up task, so readiness stays unhealthy until the schema is
/// current. A PostgreSQL advisory lock serializes replicas starting at the same time.
/// </summary>
public sealed class DatabaseMigrator(IServiceScopeFactory scopeFactory, PostgresDatabaseOptions options) : IWarmUpCommand
{
    private static readonly long AdvisoryLockKey = BitConverter.ToInt64(
        SHA256.HashData(Encoding.UTF8.GetBytes("doc-rag-ingestion-migrations")), 0);

    /// <summary>Applies every pending migration under the advisory lock. Idempotent.</summary>
    /// <returns>A task completing when the schema is current.</returns>
    public async Task Execute()
    {
        // The advisory lock is session-scoped: lock, migrate and unlock must share this connection.
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync("SELECT pg_advisory_lock(@Key);", new { Key = AdvisoryLockKey });
        try
        {
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        }
        finally
        {
            await connection.ExecuteAsync("SELECT pg_advisory_unlock(@Key);", new { Key = AdvisoryLockKey });
        }
    }
}
