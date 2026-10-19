using BlogDoFT.Libs.ResultPattern;
using Dapper;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Domain.Versions;
using DocRag.Ingestion.Infrastructure.Postgres.Tables;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class DocumentVersionRepository(DbSession session) : RepositoryBase(session), IDocumentVersionRepository
{
    private const string ResultSet = """
        SELECT v.id AS Id
             , v.public_id AS PublicId
             , v.document_id AS DocumentId
             , d.public_id AS DocumentPublicId
             , v.label AS Label
             , v.published_at AS PublishedAt
             , v.effective_from AS EffectiveFrom
             , v.effective_to AS EffectiveTo
             , v.ingestion_status AS IngestionStatus
             , v.created_at AS CreatedAt
             , v.updated_at AS UpdatedAt
             , f.id AS FileId
             , f.public_id AS FilePublicId
             , f.original_name AS FileOriginalName
             , f.content_type AS FileContentType
             , f.size_bytes AS FileSizeBytes
             , f.sha256 AS FileSha256
             , f.bucket AS FileBucket
             , f.object_key AS FileObjectKey
             , f.uploaded_at AS FileUploadedAt
        FROM document_versions v
        JOIN documents d
          ON d.id = v.document_id
        LEFT JOIN document_files f
          ON f.document_version_id = v.id
        """;

    public async Task<Result> InsertAsync(DocumentVersion version, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO document_versions
                (public_id, document_id, label, published_at, effective_from, effective_to, ingestion_status, created_at, updated_at)
            VALUES
                (@PublicId, @DocumentId, @Label, @PublishedAt, @EffectiveFrom, @EffectiveTo, @IngestionStatus, @CreatedAt, @UpdatedAt)
            RETURNING id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<long>(
                Command(sql, DocumentVersionTable.WriteParameters(version), cancellationToken));
            version.AssignId(id);
            return Result.AsSuccess();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation && ex.ConstraintName == "ux_document_versions_label")
        {
            return new Failure("400-version-label-exists", $"A version labelled '{version.Label}' already exists for this document.");
        }
    }

    public async Task<DocumentVersion?> GetAsync(
        Guid documentPublicId,
        Guid versionPublicId,
        bool forUpdate,
        CancellationToken cancellationToken = default)
    {
        var sql = $"""
            {ResultSet}
            WHERE d.public_id = @DocumentPublicId
              AND v.public_id = @VersionPublicId
            {(forUpdate ? "FOR UPDATE OF v" : string.Empty)}
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DocumentVersionTable>(Command(
            sql,
            new { DocumentPublicId = documentPublicId, VersionPublicId = versionPublicId },
            cancellationToken));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<DocumentVersion>> ListAsync(
        long documentId,
        int limit,
        Guid? after,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE v.document_id = @DocumentId
              AND (@After::uuid IS NULL OR v.public_id < @After::uuid)
            ORDER BY v.public_id DESC
            LIMIT @Limit
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DocumentVersionTable>(Command(
            sql, new { DocumentId = documentId, After = after, Limit = limit }, cancellationToken));
        return rows.Select(row => row.ToDomain()).ToList();
    }

    public async Task UpdateStatusAsync(DocumentVersion version, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE document_versions
            SET ingestion_status = @Status
              , updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(Command(
            sql,
            new
            {
                version.Id,
                Status = DocumentVersionTable.StatusToColumn(version.Status),
                UpdatedAt = version.UpdatedAt.UtcDateTime,
            },
            cancellationToken));
    }

    public async Task<int> RecoverOrphanedUploadingAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE document_versions v
            SET ingestion_status = 'upload_failed'
              , updated_at = @Now
            WHERE v.ingestion_status = 'uploading'
              AND NOT EXISTS (
                  SELECT 1
                  FROM upload_attempts a
                  WHERE a.document_version_id = v.id
                    AND a.status = 'active')
            """;

        var connection = await ConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, new { Now = now.UtcDateTime }, cancellationToken));
    }
}
