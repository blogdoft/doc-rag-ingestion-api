using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Domain.Documents;
using DocRag.Ingestion.Infrastructure.Postgres.Tables;
using Dapper;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class DocumentRepository(DbSession session) : RepositoryBase(session), IDocumentRepository
{
    private const string ResultSet = """
        SELECT id AS Id
             , public_id AS PublicId
             , title AS Title
             , description AS Description
             , type AS Type
             , language AS Language
             , tags::text AS Tags
             , metadata::text AS Metadata
             , revision AS Revision
             , created_at AS CreatedAt
             , updated_at AS UpdatedAt
        FROM documents
        """;

    public async Task InsertAsync(Document document, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO documents (public_id, title, description, type, language, tags, metadata, revision, created_at, updated_at)
            VALUES (@PublicId, @Title, @Description, @Type, @Language, @Tags::jsonb, @Metadata::jsonb, @Revision, @CreatedAt, @UpdatedAt)
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
            WHERE public_id = @PublicId
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
            WHERE (@Type::text IS NULL OR type = @Type::text)
              AND (@Language::text IS NULL OR language = @Language::text)
              AND (@Tag::text IS NULL OR tags @> jsonb_build_array(@Tag::text))
              AND (@TitlePattern::text IS NULL OR title ILIKE @TitlePattern::text ESCAPE '\')
              AND (@After::uuid IS NULL OR public_id < @After::uuid)
            ORDER BY public_id DESC
            LIMIT @Limit
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DocumentTable>(Command(
            sql,
            new
            {
                filter.Type,
                filter.Language,
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
              , type = @Type
              , language = @Language
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
                row.Type,
                row.Language,
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
