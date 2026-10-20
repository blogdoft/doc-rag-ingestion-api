using BlogDoFT.Libs.ResultPattern;
using Dapper;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Domain.MasterData;
using DocRag.Ingestion.Infrastructure.Postgres.Tables;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure.Postgres.Repositories;

internal sealed class DocumentTypeRepository(DbSession session) : RepositoryBase(session), IDocumentTypeRepository
{
    private const string NameIndex = "ux_document_types_name";

    private const string ResultSet = """
        SELECT id AS Id
             , public_id AS PublicId
             , name AS Name
             , created_at AS CreatedAt
             , updated_at AS UpdatedAt
        FROM document_types
        """;

    public async Task<Result> InsertAsync(DocumentType documentType, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO document_types (public_id, name, created_at, updated_at)
            VALUES (@PublicId, @Name, @CreatedAt, @UpdatedAt)
            RETURNING id
            """;

        var connection = await ConnectionAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<long>(
                Command(sql, DocumentTypeTable.FromDomain(documentType), cancellationToken));
            documentType.AssignId(id);
            return Result.AsSuccess();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation && ex.ConstraintName == NameIndex)
        {
            return NameTaken(documentType.Name);
        }
    }

    public async Task<DocumentType?> GetAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE public_id = @PublicId
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<DocumentTypeTable>(
            Command(sql, new { PublicId = publicId }, cancellationToken));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<DocumentType>> ListAsync(int limit, Guid? after, CancellationToken cancellationToken = default)
    {
        const string sql = $"""
            {ResultSet}
            WHERE (@After::uuid IS NULL OR public_id < @After::uuid)
            ORDER BY public_id DESC
            LIMIT @Limit
            """;

        var connection = await ConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<DocumentTypeTable>(
            Command(sql, new { After = after, Limit = limit }, cancellationToken));
        return rows.Select(row => row.ToDomain()).ToList();
    }

    public async Task<Result> UpdateAsync(DocumentType documentType, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE document_types
            SET name = @Name
              , updated_at = @UpdatedAt
            WHERE id = @Id
            """;

        var row = DocumentTypeTable.FromDomain(documentType);
        var connection = await ConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(Command(sql, new { row.Id, row.Name, row.UpdatedAt }, cancellationToken));
            return Result.AsSuccess();
        }
        catch (PostgresException ex) when (ex.SqlState == UniqueViolation && ex.ConstraintName == NameIndex)
        {
            return NameTaken(documentType.Name);
        }
    }

    public async Task<bool> DeleteAsync(Guid publicId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            DELETE FROM document_types
            WHERE public_id = @PublicId
            """;

        var connection = await ConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(Command(sql, new { PublicId = publicId }, cancellationToken)) == 1;
    }

    private static Failure NameTaken(string name) =>
        new("400-document-type-name-exists", $"A document type named '{name}' already exists.");
}
