using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.Outbox;
using DocRag.Ingestion.Domain.Documents;
using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Domain.Versions;
using BlogDoFT.Libs.ResultPattern;

namespace DocRag.Ingestion.Application.Abstractions;

/// <summary>Source of the current instant, replaceable in tests.</summary>
public interface IClock
{
    /// <summary>Gets the current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Scoped transaction boundary shared by every repository of the current scope.</summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Opens a transaction; repositories enlist automatically until commit or rollback.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the transaction has started.</returns>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits the open transaction and releases the connection.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the commit has been acknowledged.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls back the open transaction, if any, and releases the connection.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the rollback is done.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Persistence of <see cref="Document"/>.</summary>
public interface IDocumentRepository
{
    /// <summary>Inserts a document and assigns its internal key.</summary>
    /// <param name="document">The document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the row is written.</returns>
    Task InsertAsync(Document document, CancellationToken cancellationToken = default);

    /// <summary>Finds a document.</summary>
    /// <param name="publicId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document or <see langword="null"/>.</returns>
    Task<Document?> GetAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Lists documents newest first, keyset-paginated by public identifier.</summary>
    /// <param name="filter">Optional exact-match filters.</param>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="after">Only documents older than this public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The documents.</returns>
    Task<IReadOnlyList<Document>> ListAsync(
        DocumentFilter filter,
        int limit,
        Guid? after,
        CancellationToken cancellationToken = default);

    /// <summary>Persists a patched document only if the stored revision still equals <paramref name="expectedRevision"/>.</summary>
    /// <param name="document">The patched document.</param>
    /// <param name="expectedRevision">Revision read before patching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> when another writer got there first.</returns>
    Task<bool> UpdateAsync(Document document, long expectedRevision, CancellationToken cancellationToken = default);
}

/// <summary>Persistence of <see cref="DocumentVersion"/>.</summary>
public interface IDocumentVersionRepository
{
    /// <summary>Inserts a version.</summary>
    /// <param name="version">The version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or a <c>409</c> failure when the label already exists for the document.</returns>
    Task<Result> InsertAsync(DocumentVersion version, CancellationToken cancellationToken = default);

    /// <summary>Finds a version (with its file) of a document.</summary>
    /// <param name="documentPublicId">Parent document public identifier.</param>
    /// <param name="versionPublicId">Version public identifier.</param>
    /// <param name="forUpdate">Whether to lock the row until the transaction ends.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version or <see langword="null"/>.</returns>
    Task<DocumentVersion?> GetAsync(
        Guid documentPublicId,
        Guid versionPublicId,
        bool forUpdate,
        CancellationToken cancellationToken = default);

    /// <summary>Lists versions newest first, keyset-paginated by public identifier.</summary>
    /// <param name="documentId">Internal document key.</param>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="after">Only versions older than this public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The versions.</returns>
    Task<IReadOnlyList<DocumentVersion>> ListAsync(
        long documentId,
        int limit,
        Guid? after,
        CancellationToken cancellationToken = default);

    /// <summary>Persists the status transition of a version.</summary>
    /// <param name="version">The version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the row is updated.</returns>
    Task UpdateStatusAsync(DocumentVersion version, CancellationToken cancellationToken = default);

    /// <summary>Moves versions stuck in <c>uploading</c> without any active attempt to <c>upload_failed</c>.</summary>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many versions were recovered.</returns>
    Task<int> RecoverOrphanedUploadingAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Persistence of <see cref="DocumentFile"/>.</summary>
public interface IFileRepository
{
    /// <summary>Inserts the file of a version.</summary>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the row is written.</returns>
    Task InsertAsync(DocumentFile file, CancellationToken cancellationToken = default);

    /// <summary>Finds a file together with its version and document identifiers.</summary>
    /// <param name="filePublicId">File public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file or <see langword="null"/>.</returns>
    Task<DocumentFile?> GetAsync(Guid filePublicId, CancellationToken cancellationToken = default);
}

/// <summary>Persistence of <see cref="UploadAttempt"/>.</summary>
public interface IUploadAttemptRepository
{
    /// <summary>Serializes concurrent upload admission decisions until the transaction ends.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the lock is held.</returns>
    Task LockAdmissionAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks active attempts of the version whose lease ran out as expired.</summary>
    /// <param name="documentVersionId">Internal version key.</param>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when done.</returns>
    Task ExpireStaleAsync(long documentVersionId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Whether the version has an active attempt.</summary>
    /// <param name="documentVersionId">Internal version key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when one is active.</returns>
    Task<bool> HasActiveAsync(long documentVersionId, CancellationToken cancellationToken = default);

    /// <summary>Counts the active attempts.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count.</returns>
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts an attempt; fails when the version already has an active one.</summary>
    /// <param name="attempt">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or a <c>409</c> failure on a concurrent active attempt.</returns>
    Task<Result> InsertAsync(UploadAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>Persists a finished attempt, only if it is still active in the database.</summary>
    /// <param name="attempt">The attempt after <c>Complete</c> or <c>Fail</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> when the attempt was no longer active (e.g. expired).</returns>
    Task<bool> FinishAsync(UploadAttempt attempt, CancellationToken cancellationToken = default);

    /// <summary>Gets the attempt by public identifier.</summary>
    /// <param name="publicId">Attempt public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempt or <see langword="null"/>.</returns>
    Task<UploadAttempt?> GetAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Expires every active attempt whose lease ran out.</summary>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many attempts were expired.</returns>
    Task<int> ExpireAllStaleAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Lists failed or expired attempts whose temporary object was not removed yet.</summary>
    /// <param name="limit">Maximum rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The attempts.</returns>
    Task<IReadOnlyList<UploadAttempt>> ListPendingCleanupAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Records that the attempt's temporary object no longer exists.</summary>
    /// <param name="attemptId">Internal attempt key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when done.</returns>
    Task MarkCleanedAsync(long attemptId, CancellationToken cancellationToken = default);
}

/// <summary>Persistence of the transactional outbox.</summary>
public interface IOutboxRepository
{
    /// <summary>Appends a message inside the current transaction.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the row is written.</returns>
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);

    /// <summary>Claims due messages under a processing lease using <c>FOR UPDATE SKIP LOCKED</c>.</summary>
    /// <param name="batchSize">Maximum messages.</param>
    /// <param name="lease">How long the claim is exclusive.</param>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The claimed messages.</returns>
    Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
        int batchSize,
        TimeSpan lease,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a message as delivered to the broker.</summary>
    /// <param name="id">Internal message key.</param>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when done.</returns>
    Task MarkPublishedAsync(long id, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Records a failed delivery and schedules the next attempt.</summary>
    /// <param name="id">Internal message key.</param>
    /// <param name="error">Short error description.</param>
    /// <param name="nextAttemptAt">When to retry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when done.</returns>
    Task MarkFailedAsync(long id, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>Gets the number of unpublished messages and the age of the oldest one.</summary>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Backlog statistics.</returns>
    Task<OutboxBacklog> GetBacklogAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Persistence of idempotency records.</summary>
public interface IIdempotencyStore
{
    /// <summary>Reserves the key, or reports the record that already holds it.</summary>
    /// <param name="request">Reservation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    Task<IdempotencyReservation> TryReserveAsync(IdempotencyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Completes a reservation with the final fingerprint and resource.</summary>
    /// <param name="request">The key.</param>
    /// <param name="fingerprint">Final request fingerprint.</param>
    /// <param name="responseStatus">HTTP status of the original response.</param>
    /// <param name="resourceId">Public identifier of the created resource.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when done.</returns>
    Task CompleteAsync(
        IdempotencyRequest request,
        string fingerprint,
        int responseStatus,
        Guid resourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Releases an in-progress reservation so the client can retry.</summary>
    /// <param name="request">The key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when done.</returns>
    Task ReleaseAsync(IdempotencyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes expired records.</summary>
    /// <param name="now">Current instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many records were deleted.</returns>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Streaming object storage (Garage through the S3 API).</summary>
public interface IObjectStorage
{
    /// <summary>Gets the bucket originals are stored in.</summary>
    string Bucket { get; }

    /// <summary>Streams <paramref name="content"/> to <paramref name="key"/> with bounded memory.</summary>
    /// <param name="key">Opaque object key.</param>
    /// <param name="content">Source stream; read once, sequentially.</param>
    /// <param name="contentType">Validated MIME type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes stored.</returns>
    Task<long> PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Gets the stored size of an object.</summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The size, or <see langword="null"/> when the object does not exist.</returns>
    Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes an object; succeeds when it is already absent.</summary>
    /// <param name="key">Object key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when done.</returns>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Aborts multipart uploads started before <paramref name="olderThan"/>.</summary>
    /// <param name="olderThan">Cut-off instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many uploads were aborted.</returns>
    Task<int> AbortStaleMultipartUploadsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    /// <summary>Verifies the bucket exists and is accessible, without creating anything.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the check passes; throws otherwise.</returns>
    Task EnsureAccessibleAsync(CancellationToken cancellationToken = default);
}

/// <summary>Publishes outbox messages to the message broker.</summary>
public interface IEventPublisher
{
    /// <summary>Publishes one message and waits for the broker acknowledgement.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing once acknowledged; throws on failure.</returns>
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
