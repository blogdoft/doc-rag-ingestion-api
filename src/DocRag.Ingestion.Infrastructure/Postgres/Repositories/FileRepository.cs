using Dapper;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Infrastructure.Postgres.Tables;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class FileRepository(DbSession session) : RepositoryBase(session), IFileRepository
{
    public async Task InsertAsync(DocumentFile file, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO document_files
                (public_id, document_version_id, original_name, content_type, size_bytes, sha256, bucket, object_key, storage_state, uploaded_at, created_at)
            VALUES
                (@PublicId, @DocumentVersionId, @OriginalName, @ContentType, @SizeBytes, @Sha256, @Bucket, @ObjectKey, 'stored', @UploadedAt, @UploadedAt)
            RETURNING id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var id = await connection.ExecuteScalarAsync<long>(Command(sql, DocumentFileTable.FromDomain(file), cancellationToken));
        file.AssignId(id);
    }

    public async Task<DocumentFile?> GetAsync(Guid filePublicId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id AS Id
                 , public_id AS PublicId
                 , document_version_id AS DocumentVersionId
                 , original_name AS OriginalName
                 , content_type AS ContentType
                 , size_bytes AS SizeBytes
                 , sha256 AS Sha256
                 , bucket AS Bucket
                 , object_key AS ObjectKey
                 , uploaded_at AS UploadedAt
            FROM document_files
            WHERE public_id = @PublicId
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DocumentFileTable>(
            Command(sql, new { PublicId = filePublicId }, cancellationToken));
        return row?.ToDomain();
    }
}
