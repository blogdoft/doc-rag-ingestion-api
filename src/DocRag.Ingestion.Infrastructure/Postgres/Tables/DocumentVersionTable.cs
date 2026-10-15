using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Domain.Versions;

namespace DocRag.Ingestion.Infrastructure.Postgres.Tables;

/// <summary>
/// Projection joining <c>document_versions</c>, its parent's public identifier and the optional
/// <c>document_files</c> row, as read by the version queries.
/// </summary>
#pragma warning disable SA1313
internal sealed record DocumentVersionTable(
    long Id,
    Guid PublicId,
    long DocumentId,
    Guid DocumentPublicId,
    string Label,
    DateTime? PublishedAt,
    DateTime? EffectiveFrom,
    DateTime? EffectiveTo,
    string IngestionStatus,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long? FileId,
    Guid? FilePublicId,
    string? FileOriginalName,
    string? FileContentType,
    long? FileSizeBytes,
    string? FileSha256,
    string? FileBucket,
    string? FileObjectKey,
    DateTime? FileUploadedAt)
{
    public static string StatusToColumn(IngestionStatus status) => status switch
    {
        Domain.Versions.IngestionStatus.AwaitingUpload => "awaiting_upload",
        Domain.Versions.IngestionStatus.Uploading => "uploading",
        Domain.Versions.IngestionStatus.Uploaded => "uploaded",
        Domain.Versions.IngestionStatus.UploadFailed => "upload_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static object WriteParameters(DocumentVersion version) => new
    {
        PublicId = version.PublicId,
        DocumentId = version.DocumentId,
        Label = version.Label,
        PublishedAt = version.PublishedAt?.UtcDateTime,
        EffectiveFrom = version.EffectiveFrom?.UtcDateTime,
        EffectiveTo = version.EffectiveTo?.UtcDateTime,
        IngestionStatus = StatusToColumn(version.Status),
        CreatedAt = version.CreatedAt.UtcDateTime,
        UpdatedAt = version.UpdatedAt.UtcDateTime,
    };

    public DocumentVersion ToDomain()
    {
        DocumentFile? file = FileId is null
            ? null
            : DocumentFile.Rehydrate(
                FileId.Value,
                FilePublicId!.Value,
                Id,
                FileOriginalName!,
                FileContentType!,
                FileSizeBytes!.Value,
                FileSha256!.Trim(),
                FileBucket!,
                FileObjectKey!,
                DocumentTable.Utc(FileUploadedAt!.Value));

        return DocumentVersion.Rehydrate(
            Id,
            PublicId,
            DocumentId,
            DocumentPublicId,
            Label,
            PublishedAt is null ? null : DocumentTable.Utc(PublishedAt.Value),
            EffectiveFrom is null ? null : DocumentTable.Utc(EffectiveFrom.Value),
            EffectiveTo is null ? null : DocumentTable.Utc(EffectiveTo.Value),
            ParseStatus(IngestionStatus),
            file,
            DocumentTable.Utc(CreatedAt),
            DocumentTable.Utc(UpdatedAt));
    }

    private static IngestionStatus ParseStatus(string column) => column switch
    {
        "awaiting_upload" => Domain.Versions.IngestionStatus.AwaitingUpload,
        "uploading" => Domain.Versions.IngestionStatus.Uploading,
        "uploaded" => Domain.Versions.IngestionStatus.Uploaded,
        "upload_failed" => Domain.Versions.IngestionStatus.UploadFailed,
        _ => throw new InvalidOperationException($"Unknown ingestion status '{column}'."),
    };
}
#pragma warning restore SA1313
