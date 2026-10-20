using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Infrastructure.Postgres.Tables;

/// <summary>Row shape of <c>document_languages</c>.</summary>
#pragma warning disable SA1313
internal sealed record DocumentLanguageTable(long Id, Guid PublicId, string Code, DateTime CreatedAt, DateTime UpdatedAt)
{
    public static DocumentLanguageTable FromDomain(DocumentLanguage documentLanguage) => new(
        documentLanguage.Id,
        documentLanguage.PublicId,
        documentLanguage.Code,
        documentLanguage.CreatedAt.UtcDateTime,
        documentLanguage.UpdatedAt.UtcDateTime);

    public DocumentLanguage ToDomain() => DocumentLanguage.Rehydrate(
        Id,
        PublicId,
        Code,
        DocumentTable.Utc(CreatedAt),
        DocumentTable.Utc(UpdatedAt));
}
#pragma warning restore SA1313
