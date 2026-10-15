using Dapper;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

/// <summary>Shared plumbing: resolves the session's connection and enlists commands in its transaction.</summary>
internal abstract class RepositoryBase(DbSession session)
{
    protected const string UniqueViolation = "23505";

    protected async Task<NpgsqlConnection> ConnectionAsync(CancellationToken cancellationToken) =>
        await session.GetConnectionAsync(cancellationToken);

    protected CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, session.Transaction, cancellationToken: cancellationToken);
}
