using DocRag.Ingestion.Domain.Files;

namespace DocRag.Ingestion.Infrastructure.Postgres.Tables;

/// <summary>Row shape of <c>document_files</c>.</summary>
#pragma warning disable SA1313
internal sealed record DocumentFileTable(
    long Id,
    Guid PublicId,
    long DocumentVersionId,
    string OriginalName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Bucket,
    string ObjectKey,
    DateTime UploadedAt)
{
    public static DocumentFileTable FromDomain(DocumentFile file) => new(
        file.Id,
        file.PublicId,
        file.DocumentVersionId,
        file.OriginalName,
        file.ContentType,
        file.SizeBytes,
        file.Sha256,
        file.Bucket,
        file.ObjectKey,
        file.UploadedAt.UtcDateTime);

    public DocumentFile ToDomain() => DocumentFile.Rehydrate(
        Id,
        PublicId,
        DocumentVersionId,
        OriginalName,
        ContentType,
        SizeBytes,
        Sha256.Trim(),
        Bucket,
        ObjectKey,
        DocumentTable.Utc(UploadedAt));
}
#pragma warning restore SA1313
