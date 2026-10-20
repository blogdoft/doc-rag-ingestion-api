using System.Text.Json;
using DocRag.Ingestion.Domain.Documents;
using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Infrastructure.Postgres.Tables;

/// <summary>Row shape of <c>documents</c>.</summary>
#pragma warning disable SA1313
internal sealed record DocumentTable(
    long Id,
    Guid PublicId,
    string Title,
    string? Description,
    long DocumentTypeId,
    Guid TypePublicId,
    string TypeName,
    DateTime TypeCreatedAt,
    DateTime TypeUpdatedAt,
    long DocumentLanguageId,
    Guid LanguagePublicId,
    string LanguageCode,
    DateTime LanguageCreatedAt,
    DateTime LanguageUpdatedAt,
    string Tags,
    string Metadata,
    long Revision,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static DocumentTable FromDomain(Document document) => new(
        document.Id,
        document.PublicId,
        document.Title,
        document.Description,
        document.Type.Id,
        document.Type.PublicId,
        document.Type.Name,
        document.Type.CreatedAt.UtcDateTime,
        document.Type.UpdatedAt.UtcDateTime,
        document.Language.Id,
        document.Language.PublicId,
        document.Language.Code,
        document.Language.CreatedAt.UtcDateTime,
        document.Language.UpdatedAt.UtcDateTime,
        JsonSerializer.Serialize(document.Tags),
        document.Metadata.GetRawText(),
        document.Revision,
        document.CreatedAt.UtcDateTime,
        document.UpdatedAt.UtcDateTime);

    public Document ToDomain() => Document.Rehydrate(
        Id,
        PublicId,
        Title,
        Description,
        DocumentType.Rehydrate(DocumentTypeId, TypePublicId, TypeName, Utc(TypeCreatedAt), Utc(TypeUpdatedAt)),
        DocumentLanguage.Rehydrate(DocumentLanguageId, LanguagePublicId, LanguageCode, Utc(LanguageCreatedAt), Utc(LanguageUpdatedAt)),
        JsonSerializer.Deserialize<List<string>>(Tags) ?? [],
        JsonSerializer.Deserialize<JsonElement>(Metadata),
        Revision,
        Utc(CreatedAt),
        Utc(UpdatedAt));

    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
#pragma warning restore SA1313
