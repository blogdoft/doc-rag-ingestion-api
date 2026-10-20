using BlogDoFT.Libs.ResultPattern;
using Dapper;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Domain.MasterData;
using DocRag.Ingestion.Infrastructure.Postgres.Tables;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class DocumentLanguageRepository(DbSession session) : RepositoryBase(session), IDocumentLanguageRepository
{
    private const string CodeIndex = "ux_document_languages_code";

    private const string ResultSet = """
        SELECT id AS Id
             , public_id AS PublicId
             , code AS Code
             , created_at AS CreatedAt
             , updated_at AS UpdatedAt
        FROM document_languages
        """;

    public async Task<Result> InsertAsync(DocumentLanguage documentLanguage, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO document_languages (public_id, code, created_at, updated_at)
            VALUES (@PublicId, @Code, @CreatedAt, @UpdatedAt)
            RETURNING id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<long>(
                Command(sql, DocumentLanguageTable.FromDomain(documentLanguage), cancellationToken));
            documentLanguage.AssignId(id);
            return Result.AsSuccess();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation && ex.ConstraintName == CodeIndex)
        {
            return CodeTaken(documentLanguage.Code);
        }
    }

    public async Task<DocumentLanguage?> GetAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE public_id = @PublicId
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DocumentLanguageTable>(
            Command(sql, new { PublicId = publicId }, cancellationToken));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<DocumentLanguage>> ListAsync(int limit, Guid? after, CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE (@After::uuid IS NULL OR public_id < @After::uuid)
            ORDER BY public_id DESC
            LIMIT @Limit
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DocumentLanguageTable>(
            Command(sql, new { After = after, Limit = limit }, cancellationToken));
        return rows.Select(row => row.ToDomain()).ToList();
    }

    public async Task<Result> UpdateAsync(DocumentLanguage documentLanguage, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE document_languages
            SET code = @Code
              , updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        var row = DocumentLanguageTable.FromDomain(documentLanguage);
        var connection = await ConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(Command(sql, new { row.Id, row.Code, row.UpdatedAt }, cancellationToken));
            return Result.AsSuccess();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation && ex.ConstraintName == CodeIndex)
        {
            return CodeTaken(documentLanguage.Code);
        }
    }

    public async Task<bool> DeleteAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM document_languages
            WHERE public_id = @PublicId
            """;

        var connection = await ConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, new { PublicId = publicId }, cancellationToken)) == 1;
    }

    private static Failure CodeTaken(string code) =>
        new("400-document-language-code-exists", $"A document language with code '{code}' already exists.");
}
