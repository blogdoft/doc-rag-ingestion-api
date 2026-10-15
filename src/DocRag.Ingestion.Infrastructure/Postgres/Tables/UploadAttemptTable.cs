using DocRag.Ingestion.Domain.Files;

namespace DocRag.Ingestion.Infrastructure.Postgres.Tables;

/// <summary>Row shape of <c>upload_attempts</c>.</summary>
#pragma warning disable SA1313
internal sealed record UploadAttemptTable(
    long Id,
    Guid PublicId,
    long DocumentVersionId,
    Guid FileId,
    string Status,
    string TemporaryObjectKey,
    DateTime LeaseExpiresAt,
    long BytesReceived,
    string? FailureCode,
    DateTime CreatedAt)
{
    public static UploadAttemptTable FromDomain(UploadAttempt attempt) => new(
        attempt.Id,
        attempt.PublicId,
        attempt.DocumentVersionId,
        attempt.FileId,
        StatusToColumn(attempt.Status),
        attempt.TemporaryObjectKey,
        attempt.LeaseExpiresAt.UtcDateTime,
        attempt.BytesReceived,
        attempt.FailureCode,
        attempt.CreatedAt.UtcDateTime);

    public static string StatusToColumn(UploadAttemptStatus status) => status switch
    {
        UploadAttemptStatus.Active => "active",
        UploadAttemptStatus.Completed => "completed",
        UploadAttemptStatus.Failed => "failed",
        UploadAttemptStatus.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public UploadAttempt ToDomain() => UploadAttempt.Rehydrate(
        Id,
        PublicId,
        DocumentVersionId,
        FileId,
        Status switch
        {
            "active" => UploadAttemptStatus.Active,
            "completed" => UploadAttemptStatus.Completed,
            "failed" => UploadAttemptStatus.Failed,
            "expired" => UploadAttemptStatus.Expired,
            _ => throw new InvalidOperationException($"Unknown upload attempt status '{Status}'."),
        },
        TemporaryObjectKey,
        DocumentTable.Utc(LeaseExpiresAt),
        BytesReceived,
        FailureCode,
        DocumentTable.Utc(CreatedAt));
}
#pragma warning restore SA1313
