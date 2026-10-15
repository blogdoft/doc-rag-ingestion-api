using Dapper;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Outbox;
using DocRag.Ingestion.Infrastructure.Postgres.Tables;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class OutboxRepository(DbSession session) : RepositoryBase(session), IOutboxRepository
{
    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO outbox_messages (event_id, event_type, aggregate_id, payload, occurred_at, attempts, next_attempt_at)
            VALUES (@EventId, @EventType, @AggregateId, @Payload::jsonb, @OccurredAt, 0, @OccurredAt)
            """;

        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(sql, OutboxMessageTable.FromDomain(message), cancellationToken));
    }

    public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lease,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH due AS (
                SELECT id
                FROM outbox_messages
                WHERE published_at IS NULL
                  AND next_attempt_at <= @Now
                  AND (lease_until IS NULL OR lease_until <= @Now)
                ORDER BY id
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE outbox_messages m
            SET lease_until = @LeaseUntil
            FROM due
            WHERE m.id = due.id
            RETURNING m.id AS Id
                    , m.event_id AS EventId
                    , m.event_type AS EventType
                    , m.aggregate_id AS AggregateId
                    , m.payload::text AS Payload
                    , m.occurred_at AS OccurredAt
                    , m.attempts AS Attempts
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<OutboxMessageTable>(Command(
            sql,
            new { Now = now.UtcDateTime, BatchSize = batchSize, LeaseUntil = (now + lease).UtcDateTime },
            cancellationToken));
        return rows.OrderBy(row => row.Id).Select(row => row.ToDomain()).ToList();
    }

    public async Task MarkPublishedAsync(long id, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE outbox_messages
            SET published_at = @Now
              , lease_until = NULL
              , last_error = NULL
            WHERE id = @Id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(sql, new { Id = id, Now = now.UtcDateTime }, cancellationToken));
    }

    public async Task MarkFailedAsync(long id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE outbox_messages
            SET attempts = attempts + 1
              , last_error = @Error
              , next_attempt_at = @NextAttemptAt
              , lease_until = NULL
            WHERE id = @Id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(
            Command(sql, new { Id = id, Error = error, NextAttemptAt = nextAttemptAt.UtcDateTime }, cancellationToken));
    }

    public async Task<OutboxBacklog> GetBacklogAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT count(*) AS Pending
                 , min(occurred_at) AS Oldest
            FROM outbox_messages
            WHERE published_at IS NULL
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleAsync<BacklogRow>(Command(sql, null, cancellationToken));
        var oldest = row.Oldest is null ? TimeSpan.Zero : now - DocumentTable.Utc(row.Oldest.Value);
        return new OutboxBacklog(row.Pending, oldest);
    }

    private sealed record BacklogRow(long Pending, DateTime? Oldest);
}
