using BlogDoFT.Libs.ResultPattern;
using Dapper;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Infrastructure.Postgres.Tables;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class UploadAttemptRepository(DbSession session) : RepositoryBase(session), IUploadAttemptRepository
{
    private const string ResultSet = """
        SELECT id AS Id
             , public_id AS PublicId
             , document_version_id AS DocumentVersionId
             , file_id AS FileId
             , status AS Status
             , temporary_object_key AS TemporaryObjectKey
             , lease_expires_at AS LeaseExpiresAt
             , bytes_received AS BytesReceived
             , failure_code AS FailureCode
             , created_at AS CreatedAt
        FROM upload_attempts
        """;

    public async Task LockAdmissionAsync(CancellationToken cancellationToken = default)
    {
        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(
            "SELECT pg_advisory_xact_lock(hashtextextended(@Key, 0))", new { Key = "upload-admission" }, cancellationToken));
    }

    public async Task ExpireStaleAsync(long documentVersionId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE upload_attempts
            SET status = 'expired'
              , failure_code = 'lease-expired'
              , updated_at = @Now
            WHERE document_version_id = @DocumentVersionId
              AND status = 'active'
              AND lease_expires_at <= @Now
            """;

        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(sql, new { DocumentVersionId = documentVersionId, Now = now.UtcDateTime }, cancellationToken));
    }

    public async Task<bool> HasActiveAsync(long documentVersionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM upload_attempts
                WHERE document_version_id = @DocumentVersionId
                  AND status = 'active')
            """;

        var connection = await ConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(Command(sql, new { DocumentVersionId = documentVersionId }, cancellationToken));
    }

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT count(*)
            FROM upload_attempts
            WHERE status = 'active'
            """;

        var connection = await ConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(Command(sql, null, cancellationToken));
    }

    public async Task<Result> InsertAsync(UploadAttempt attempt, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO upload_attempts
                (public_id, document_version_id, file_id, status, temporary_object_key, lease_expires_at, bytes_received, created_at, updated_at)
            VALUES
                (@PublicId, @DocumentVersionId, @FileId, @Status, @TemporaryObjectKey, @LeaseExpiresAt, @BytesReceived, @CreatedAt, @CreatedAt)
            RETURNING id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<long>(Command(sql, UploadAttemptTable.FromDomain(attempt), cancellationToken));
            attempt.AssignId(id);
            return Result.AsSuccess();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation)
        {
            return new Failure("400-upload-in-progress", "Another upload is already in progress for this version.");
        }
    }

    public async Task<bool> FinishAsync(UploadAttempt attempt, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE upload_attempts
            SET status = @Status
              , bytes_received = @BytesReceived
              , failure_code = @FailureCode
              , updated_at = now()
            WHERE id = @Id
              AND status = 'active'
            """;

        var row = UploadAttemptTable.FromDomain(attempt);
        var connection = await ConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            Command(sql, new { row.Id, row.Status, row.BytesReceived, row.FailureCode }, cancellationToken));
        return affected == 1;
    }

    public async Task<UploadAttempt?> GetAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE public_id = @PublicId
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UploadAttemptTable>(Command(sql, new { PublicId = publicId }, cancellationToken));
        return row?.ToDomain();
    }

    public async Task<int> ExpireAllStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE upload_attempts
            SET status = 'expired'
              , failure_code = 'lease-expired'
              , updated_at = @Now
            WHERE status = 'active'
              AND lease_expires_at <= @Now
            """;

        var connection = await ConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, new { Now = now.UtcDateTime }, cancellationToken));
    }

    public async Task<IReadOnlyList<UploadAttempt>> ListPendingCleanupAsync(int limit, CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE status IN ('failed', 'expired')
              AND NOT object_cleaned
            ORDER BY id
            LIMIT @Limit
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UploadAttemptTable>(Command(sql, new { Limit = limit }, cancellationToken));
        return rows.Select(row => row.ToDomain()).ToList();
    }

    public async Task MarkCleanedAsync(long attemptId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE upload_attempts
            SET object_cleaned = TRUE
              , updated_at = now()
            WHERE id = @Id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(sql, new { Id = attemptId }, cancellationToken));
    }
}
