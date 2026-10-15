using System.Text.Json;
using DocRag.Ingestion.Domain.Events;

namespace DocRag.Ingestion.Application.Outbox;

/// <summary>Serializes <see cref="DocumentUploadedEvent"/> to the wire contract of <c>event-document-uploaded.schema.json</c>.</summary>
public static class DocumentUploadedPayload
{
    /// <summary>Event type name.</summary>
    public const string EventType = "DocumentUploaded";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the outbox message of the event, inside the caller's transaction.</summary>
    /// <param name="domainEvent">The domain event.</param>
    /// <returns>The message ready to be appended to the outbox.</returns>
    public static OutboxMessage ToOutboxMessage(DocumentUploadedEvent domainEvent) => new(
        0,
        domainEvent.EventId,
        EventType,
        domainEvent.DocumentId,
        JsonSerializer.Serialize(
            new
            {
                eventId = domainEvent.EventId,
                eventType = EventType,
                eventVersion = 1,
                occurredAt = domainEvent.OccurredAt.UtcDateTime,
                correlationId = domainEvent.CorrelationId,
                documentId = domainEvent.DocumentId,
                versionId = domainEvent.VersionId,
                fileId = domainEvent.FileId,
                contentType = domainEvent.ContentType,
                sizeBytes = domainEvent.SizeBytes,
                sha256 = domainEvent.Sha256,
                storage = new { provider = "garage-s3", bucket = domainEvent.Bucket, objectKey = domainEvent.ObjectKey },
            },
            Options),
        domainEvent.OccurredAt,
        0);
}
