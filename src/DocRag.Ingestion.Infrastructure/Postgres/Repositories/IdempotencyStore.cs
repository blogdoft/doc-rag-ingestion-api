using Dapper;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Idempotency;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class IdempotencyStore(DbSession session) : RepositoryBase(session), IIdempotencyStore
{
    public async Task<IdempotencyReservation> TryReserveAsync(IdempotencyRequest request, CancellationToken cancellationToken = default)
    {
        const string reserve = """
            INSERT INTO idempotency_records (operation, idempotency_key, request_fingerprint, state, expires_at, created_at)
            VALUES (@Operation, @Key, @Fingerprint, 'in_progress', @ExpiresAt, @Now)
            ON CONFLICT (operation, idempotency_key) DO UPDATE
            SET request_fingerprint = EXCLUDED.request_fingerprint
              , state = 'in_progress'
              , response_status = NULL
              , resource_id = NULL
              , expires_at = EXCLUDED.expires_at
              , created_at = EXCLUDED.created_at
            WHERE idempotency_records.expires_at <= @Now
            RETURNING 1
            """;

        const string existing = """
            SELECT state = 'completed' AS Completed
                 , request_fingerprint AS Fingerprint
                 , resource_id AS ResourceId
            FROM idempotency_records
            WHERE operation = @Operation
              AND idempotency_key = @Key
            """;

        var parameters = new
        {
            request.Operation,
            request.Key,
            request.Fingerprint,
            ExpiresAt = request.ExpiresAt.UtcDateTime,
            Now = request.Now.UtcDateTime,
        };

        var connection = await ConnectionAsync(cancellationToken);
        var reserved = await connection.ExecuteScalarAsync<int?>(Command(reserve, parameters, cancellationToken));
        if (reserved is not null)
        {
            return new IdempotencyReservation(true, null);
        }

        var record = await connection.QuerySingleAsync<IdempotencyRecord>(Command(existing, parameters, cancellationToken));
        return new IdempotencyReservation(false, record);
    }

    public async Task CompleteAsync(
        IdempotencyRequest request,
        string fingerprint,
        int responseStatus,
        Guid resourceId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE idempotency_records
            SET state = 'completed'
              , request_fingerprint = @Fingerprint
              , response_status = @ResponseStatus
              , resource_id = @ResourceId
              , expires_at = @RetainUntil
            WHERE operation = @Operation
              AND idempotency_key = @Key
            """;

        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(
            sql,
            new
            {
                request.Operation,
                request.Key,
                Fingerprint = fingerprint,
                ResponseStatus = responseStatus,
                ResourceId = resourceId,
                RetainUntil = request.RetainUntil.UtcDateTime,
            },
            cancellationToken));
    }

    public async Task ReleaseAsync(IdempotencyRequest request, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM idempotency_records
            WHERE operation = @Operation
              AND idempotency_key = @Key
              AND state = 'in_progress'
            """;

        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(sql, new { request.Operation, request.Key }, cancellationToken));
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM idempotency_records
            WHERE expires_at <= @Now
            """;

        var connection = await ConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, new { Now = now.UtcDateTime }, cancellationToken));
    }
}
