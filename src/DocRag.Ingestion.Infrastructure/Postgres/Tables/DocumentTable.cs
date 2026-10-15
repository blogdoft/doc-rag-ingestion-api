using System.Text.Json;
using DocRag.Ingestion.Domain.Documents;

namespace DocRag.Ingestion.Infrastructure.Postgres.Tables;

/// <summary>Row shape of <c>documents</c>.</summary>
#pragma warning disable SA1313
internal sealed record DocumentTable(
    long Id,
    Guid PublicId,
    string Title,
    string? Description,
    string Type,
    string Language,
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
        document.Type,
        document.Language,
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
        Type,
        Language,
        JsonSerializer.Deserialize<List<string>>(Tags) ?? [],
        JsonSerializer.Deserialize<JsonElement>(Metadata),
        Revision,
        Utc(CreatedAt),
        Utc(UpdatedAt));

    internal static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
#pragma warning restore SA1313
