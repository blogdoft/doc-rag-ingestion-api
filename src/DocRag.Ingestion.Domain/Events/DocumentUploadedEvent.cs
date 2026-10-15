namespace DocRag.Ingestion.Domain.Events;

/// <summary>Fact recorded when a version's original file was stored and registered.</summary>
/// <param name="EventId">Unique event identifier, the consumers' deduplication key.</param>
/// <param name="OccurredAt">When the ingestion completed (UTC).</param>
/// <param name="CorrelationId">Request correlation identifier.</param>
/// <param name="DocumentId">Public document identifier.</param>
/// <param name="VersionId">Public version identifier.</param>
/// <param name="FileId">Public file identifier.</param>
/// <param name="ContentType">Validated MIME type.</param>
/// <param name="SizeBytes">Stored size.</param>
/// <param name="Sha256">Lowercase hexadecimal digest.</param>
/// <param name="Bucket">Storage bucket.</param>
/// <param name="ObjectKey">Storage object key.</param>
public sealed record DocumentUploadedEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    Guid DocumentId,
    Guid VersionId,
    Guid FileId,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string Bucket,
    string ObjectKey);
