namespace DocRag.Ingestion.Application.Idempotency;

/// <summary>Identifies an idempotency key within its operation scope.</summary>
/// <param name="Operation">Operation and target, e.g. <c>upload-file:{documentId}:{versionId}</c>.</param>
/// <param name="Key">Client supplied key.</param>
/// <param name="Fingerprint">Request fingerprint known upfront, or <see langword="null"/> when only known at the end.</param>
/// <param name="Now">Current instant.</param>
/// <param name="ExpiresAt">Expiry of the in-progress reservation.</param>
/// <param name="RetainUntil">Expiry of the record once completed.</param>
public sealed record IdempotencyRequest(
    string Operation,
    string Key,
    string? Fingerprint,
    DateTimeOffset Now,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RetainUntil);

/// <summary>A stored idempotency record.</summary>
/// <param name="Completed">Whether the original operation finished.</param>
/// <param name="Fingerprint">Fingerprint of the original request, when known.</param>
/// <param name="ResourceId">Public identifier of the resource the original operation produced.</param>
public sealed record IdempotencyRecord(bool Completed, string? Fingerprint, Guid? ResourceId);

/// <summary>Outcome of trying to reserve an idempotency key.</summary>
/// <param name="Reserved">Whether the caller now owns the key.</param>
/// <param name="Existing">The record that already holds the key, when not reserved.</param>
public sealed record IdempotencyReservation(bool Reserved, IdempotencyRecord? Existing);
