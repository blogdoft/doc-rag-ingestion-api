using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Infrastructure.Postgres.Tables;

/// <summary>Row shape of <c>document_types</c>.</summary>
#pragma warning disable SA1313
internal sealed record DocumentTypeTable(long Id, Guid PublicId, string Name, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static DocumentTypeTable FromDomain(DocumentType documentType) => new(
        documentType.Id,
        documentType.PublicId,
        documentType.Name,
        documentType.CreatedAt.UtcDateTime,
        documentType.UpdatedAt.UtcDateTime);

    public DocumentType ToDomain() => DocumentType.Rehydrate(
        Id,
        PublicId,
        Name,
        DocumentTable.Utc(CreatedAt),
        DocumentTable.Utc(UpdatedAt));
}
#pragma warning restore SA1313
