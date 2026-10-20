using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Domain.Documents;
using DocRag.Ingestion.Infrastructure.Postgres.Tables;
using Dapper;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class DocumentRepository(DbSession session) : RepositoryBase(session), IDocumentRepository
{
    private const string ResultSet = """
        SELECT d.id AS Id
             , d.public_id AS PublicId
             , d.title AS Title
             , d.description AS Description
             , t.id AS DocumentTypeId
             , t.public_id AS TypePublicId
             , t.name AS TypeName
             , t.created_at AS TypeCreatedAt
             , t.updated_at AS TypeUpdatedAt
             , l.id AS DocumentLanguageId
             , l.public_id AS LanguagePublicId
             , l.code AS LanguageCode
             , l.created_at AS LanguageCreatedAt
             , l.updated_at AS LanguageUpdatedAt
             , d.tags::text AS Tags
             , d.metadata::text AS Metadata
             , d.revision AS Revision
             , d.created_at AS CreatedAt
             , d.updated_at AS UpdatedAt
        FROM documents d
        JOIN document_types t
          ON t.id = d.document_type_id
        JOIN document_languages l
          ON l.id = d.document_language_id
        """;

    public async Task InsertAsync(Document document, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO documents (public_id, title, description, document_type_id, document_language_id, tags, metadata, revision, created_at, updated_at)
            VALUES (@PublicId, @Title, @Description, @DocumentTypeId, @DocumentLanguageId, @Tags::jsonb, @Metadata::jsonb, @Revision, @CreatedAt, @UpdatedAt)
            RETURNING id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var id = await connection.ExecuteScalarAsync<long>(Command(sql, DocumentTable.FromDomain(document), cancellationToken));
        document.AssignId(id);
    }

    public async Task<Document?> GetAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE d.public_id = @PublicId
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DocumentTable>(
            Command(sql, new { PublicId = publicId }, cancellationToken));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Document>> ListAsync(
        DocumentFilter filter,
        int limit,
        Guid? after,
        CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE (@TypeId::uuid IS NULL OR t.public_id = @TypeId::uuid)
              AND (@LanguageId::uuid IS NULL OR l.public_id = @LanguageId::uuid)
              AND (@Tag::text IS NULL OR d.tags @> jsonb_build_array(@Tag::text))
              AND (@TitlePattern::text IS NULL OR d.title ILIKE @TitlePattern::text ESCAPE '\')
              AND (@After::uuid IS NULL OR d.public_id < @After::uuid)
            ORDER BY d.public_id DESC
            LIMIT @Limit
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DocumentTable>(Command(
            sql,
            new
            {
                filter.TypeId,
                filter.LanguageId,
                filter.Tag,
                TitlePattern = filter.Title is null ? null : WildcardPattern.ToLikePattern(filter.Title),
                After = after,
                Limit = limit,
            },
            cancellationToken));
        return rows.Select(row => row.ToDomain()).ToList();
    }

    public async Task<bool> UpdateAsync(Document document, long expectedRevision, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE documents
            SET title = @Title
              , description = @Description
              , document_type_id = @DocumentTypeId
              , document_language_id = @DocumentLanguageId
              , tags = @Tags::jsonb
              , metadata = @Metadata::jsonb
              , revision = @Revision
              , updated_at = @UpdatedAt
            WHERE id = @Id
              AND revision = @ExpectedRevision
            """;

        var row = DocumentTable.FromDomain(document);
        var connection = await ConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(Command(
            sql,
            new
            {
                row.Id,
                row.Title,
                row.Description,
                row.DocumentTypeId,
                row.DocumentLanguageId,
                row.Tags,
                row.Metadata,
                row.Revision,
                row.UpdatedAt,
                ExpectedRevision = expectedRevision,
            },
            cancellationToken));
        return affected == 1;
    }
}
