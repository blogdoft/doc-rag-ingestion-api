using System.Diagnostics;
using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Outbox;
using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Domain.Versions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Application.Files;

/// <summary>Input of <see cref="FileUploadService.UploadAsync"/>.</summary>
/// <param name="DocumentId">Parent document public identifier.</param>
/// <param name="VersionId">Version public identifier.</param>
/// <param name="IdempotencyKey">Client key, if any.</param>
/// <param name="FileName">Raw file name from <c>Content-Disposition</c>.</param>
/// <param name="ContentType">Declared media type, without parameters.</param>
/// <param name="ExpectedSha256">Optional digest announced by the client.</param>
/// <param name="CorrelationId">Request correlation identifier.</param>
/// <param name="Body">The raw request body stream.</param>
public sealed record UploadFileCommand(
    Guid DocumentId,
    Guid VersionId,
    string? IdempotencyKey,
    string? FileName,
    string ContentType,
    string? ExpectedSha256,
    string? CorrelationId,
    Stream Body);

/// <summary>
/// Orchestrates the upload: admission under a lease, streaming to object storage with bounded
/// memory, integrity checks and an atomic commit of file, version status and outbox message.
/// Object storage and PostgreSQL never share a transaction, so failures are handled through the
/// attempt state machine and later reconciliation.
/// </summary>
public sealed class FileUploadService(
    IDocumentVersionRepository versions,
    IFileRepository files,
    IUploadAttemptRepository attempts,
    IOutboxRepository outbox,
    IIdempotencyStore idempotencyStore,
    IdempotencyGate idempotency,
    IObjectStorage storage,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<UploadOptions> options,
    ILogger<FileUploadService> logger)
{
    /// <summary>Uploads the original file of a version.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored file, or a failure whose code starts with the HTTP status.</returns>
    public async Task<Result<DocumentFile>> UploadAsync(UploadFileCommand command, CancellationToken cancellationToken)
    {
        var policy = options.Value;
        var contentType = command.ContentType.ToLowerInvariant();
        if (!policy.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return new Failure("400-content-type-not-allowed", $"The media type '{contentType}' is not accepted.");
        }

        var fileName = FileNameSanitizer.Sanitize(command.FileName);
        if (fileName.IsFailure)
        {
            return fileName.Failure;
        }

        var expectedSha = command.ExpectedSha256?.ToLowerInvariant();
        var operation = $"upload-file:{command.DocumentId}:{command.VersionId}";
        var lease = TimeSpan.FromMinutes(policy.LeaseMinutes);
        var request = idempotency.Describe(operation, command.IdempotencyKey, null, lease);

        var admission = await AdmitAsync(command, request, lease, cancellationToken);
        if (admission.IsFailure)
        {
            return admission.Failure;
        }

        if (admission.Value.ReplayFileId is { } replayFileId)
        {
            return await ReplayAsync(command, replayFileId, expectedSha, policy, cancellationToken);
        }

        var attempt = admission.Value.Attempt!;
        var started = Stopwatch.GetTimestamp();
        IngestionTelemetry.ActiveUploads.Add(1);
        var stream = new HashingLimitedStream(command.Body, policy.MaxBytes);
        try
        {
            var stored = await StreamToStorageAsync(stream, attempt, contentType, cancellationToken);
            if (stored.IsFailure)
            {
                return await AbortAsync(command, attempt, request, stored.Failure, stream.BytesRead, cancellationToken);
            }

            var digest = stream.GetDigest();
            if (expectedSha is not null && expectedSha != digest)
            {
                return await AbortAsync(
                    command, attempt, request, new Failure("400-checksum-mismatch", "The SHA-256 of the received bytes differs from X-Content-SHA256."),
                    stream.BytesRead, cancellationToken);
            }

            var confirmedSize = await storage.GetSizeAsync(attempt.TemporaryObjectKey, cancellationToken);
            if (confirmedSize != stream.BytesRead)
            {
                return await AbortAsync(
                    command, attempt, request, new Failure("503-storage-verification-failed", "The stored object could not be verified."),
                    stream.BytesRead, cancellationToken);
            }

            var finished = await FinalizeAsync(command, attempt, request, fileName.Value, contentType, digest, stream.BytesRead, cancellationToken);
            IngestionTelemetry.UploadsTotal.Add(1, new KeyValuePair<string, object?>("outcome", finished.IsSuccess ? "success" : "failure"));
            if (finished.IsSuccess)
            {
                IngestionTelemetry.UploadBytes.Record(stream.BytesRead);
            }

            return finished;
        }
        catch (OperationCanceledException)
        {
            await AbortSafelyAsync(command, attempt, request, "client-cancelled", stream.BytesRead);
            throw;
        }
        finally
        {
            IngestionTelemetry.ActiveUploads.Add(-1);
            IngestionTelemetry.UploadDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
            await stream.DisposeAsync();
        }
    }

    private async Task<Result<Admission>> AdmitAsync(
        UploadFileCommand command,
        IdempotencyRequest? request,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        await using (unitOfWork)
        {
            await unitOfWork.BeginAsync(cancellationToken);

            var version = await versions.GetAsync(command.DocumentId, command.VersionId, true, cancellationToken);
            if (version is null)
            {
                return new Failure("404-version-not-found", "Version not found.");
            }

            var decision = await idempotency.EnterAsync(request, cancellationToken);
            if (decision.Failure is not null)
            {
                return decision.Failure;
            }

            if (decision.IsReplay)
            {
                return new Admission(null, decision.ReplayResourceId);
            }

            var now = clock.UtcNow;
            await attempts.ExpireStaleAsync(version.Id, now, cancellationToken);
            var active = await attempts.HasActiveAsync(version.Id, cancellationToken);

            await attempts.LockAdmissionAsync(cancellationToken);
            if (await attempts.CountActiveAsync(cancellationToken) >= options.Value.MaxConcurrentUploads)
            {
                return new Failure("429-too-many-uploads", "The service reached its limit of concurrent uploads.");
            }

            var started = version.StartUpload(active, now);
            if (started.IsFailure)
            {
                return started.Failure;
            }

            var fileId = Guid.CreateVersion7(now);
            var attempt = UploadAttempt.Start(
                Guid.CreateVersion7(now),
                version.Id,
                fileId,
                $"{command.DocumentId}/{command.VersionId}/{fileId}",
                now,
                lease);

            var inserted = await attempts.InsertAsync(attempt, cancellationToken);
            if (inserted.IsFailure)
            {
                return inserted.Failure;
            }

            await versions.UpdateStatusAsync(version, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new Admission(attempt, null);
        }
    }

    private async Task<Result<bool>> StreamToStorageAsync(
        HashingLimitedStream stream,
        UploadAttempt attempt,
        string contentType,
        CancellationToken cancellationToken)
    {
        try
        {
            var header = new byte[FileSignature.HeaderLength];
            var filled = 0;
            while (filled < header.Length)
            {
                var read = await stream.ReadAsync(header.AsMemory(filled), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            if (filled == 0)
            {
                return new Failure("400-file-empty", "The upload body is empty.");
            }

            if (!FileSignature.Matches(contentType, header.AsSpan(0, filled)))
            {
                return new Failure("400-file-signature-mismatch", "The file content does not match the declared media type.");
            }

            await using var replay = new PrefixedStream(header.AsMemory(0, filled), stream);
            await storage.PutAsync(attempt.TemporaryObjectKey, replay, contentType, cancellationToken);
            return true;
        }
        catch (UploadTooLargeException ex)
        {
            return new Failure("400-file-too-large", $"The upload exceeds the limit of {ex.Limit} bytes.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Object storage failed for upload attempt {AttemptId}", attempt.PublicId);
            return new Failure("503-storage-unavailable", "The object storage is unavailable.");
        }
    }

    private async Task<Result<DocumentFile>> FinalizeAsync(
        UploadFileCommand command,
        UploadAttempt attempt,
        IdempotencyRequest? request,
        string fileName,
        string contentType,
        string digest,
        long size,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var file = DocumentFile.Create(
            attempt.FileId, attempt.DocumentVersionId, fileName, contentType, size, digest,
            storage.Bucket, attempt.TemporaryObjectKey, now);
        if (file.IsFailure)
        {
            return await AbortAsync(command, attempt, request, file.Failure, size, cancellationToken);
        }

        await using (unitOfWork)
        {
            await unitOfWork.BeginAsync(cancellationToken);

            var version = await versions.GetAsync(command.DocumentId, command.VersionId, true, cancellationToken);
            var completed = attempt.Complete(size);
            if (version is null || completed.IsFailure || !await attempts.FinishAsync(attempt, cancellationToken))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return await AbortAsync(
                    command, attempt, request,
                    new Failure("400-upload-attempt-not-active", "The upload lease expired before the upload finished."),
                    size, cancellationToken, markAttempt: false);
            }

            await files.InsertAsync(file.Value, cancellationToken);
            var marked = version.MarkUploaded(file.Value, Guid.CreateVersion7(now), command.CorrelationId, now);
            if (marked.IsFailure)
            {
                return marked.Failure;
            }

            await versions.UpdateStatusAsync(version, cancellationToken);
            foreach (var domainEvent in version.Events)
            {
                await outbox.AddAsync(DocumentUploadedPayload.ToOutboxMessage(domainEvent), cancellationToken);
            }

            if (request is not null)
            {
                await idempotencyStore.CompleteAsync(request, digest, 201, file.Value.PublicId, cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }

        return file;
    }

    private async Task<Result<DocumentFile>> ReplayAsync(
        UploadFileCommand command,
        Guid fileId,
        string? expectedSha,
        UploadOptions policy,
        CancellationToken cancellationToken)
    {
        var original = await files.GetAsync(fileId, cancellationToken);
        if (original is null)
        {
            return new Failure("404-file-not-found", "The original file no longer exists.");
        }

        if (expectedSha is not null && expectedSha != original.Sha256)
        {
            return IdempotencyGate.KeyReused();
        }

        await using var hashing = new HashingLimitedStream(command.Body, policy.MaxBytes);
        try
        {
            await hashing.CopyToAsync(Stream.Null, cancellationToken);
        }
        catch (UploadTooLargeException)
        {
            return IdempotencyGate.KeyReused();
        }

        return hashing.GetDigest() == original.Sha256 ? original : IdempotencyGate.KeyReused();
    }

    private async Task<Failure> AbortAsync(
        UploadFileCommand command,
        UploadAttempt attempt,
        IdempotencyRequest? request,
        Failure failure,
        long bytesReceived,
        CancellationToken cancellationToken,
        bool markAttempt = true)
    {
        IngestionTelemetry.UploadFailures.Add(1, new KeyValuePair<string, object?>("reason", failure.Code));
        IngestionTelemetry.UploadsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "failure"));
        try
        {
            await storage.DeleteAsync(attempt.TemporaryObjectKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The attempt row keeps the object reconcilable if this cleanup fails.
            logger.LogWarning(ex, "Could not delete temporary object of attempt {AttemptId}; reconciliation will retry", attempt.PublicId);
            markAttempt = true;
        }

        await RecordFailureAsync(command, attempt, request, failure.Code, bytesReceived, markAttempt, cancellationToken);
        return failure;
    }

    private async Task AbortSafelyAsync(
        UploadFileCommand command,
        UploadAttempt attempt,
        IdempotencyRequest? request,
        string reason,
        long bytesReceived)
    {
        try
        {
            await AbortAsync(command, attempt, request, new Failure(reason, reason), bytesReceived, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record the cancelled upload attempt {AttemptId}; its lease will expire", attempt.PublicId);
        }
    }

    private async Task RecordFailureAsync(
        UploadFileCommand command,
        UploadAttempt attempt,
        IdempotencyRequest? request,
        string failureCode,
        long bytesReceived,
        bool markAttempt,
        CancellationToken cancellationToken)
    {
        await using (unitOfWork)
        {
            await unitOfWork.BeginAsync(CancellationToken.None);
            var version = await versions.GetAsync(command.DocumentId, command.VersionId, true, CancellationToken.None);
            if (markAttempt)
            {
                attempt.Fail(failureCode, bytesReceived);
                await attempts.FinishAsync(attempt, CancellationToken.None);
            }

            if (version is not null && version.Status == IngestionStatus.Uploading)
            {
                version.MarkUploadFailed(clock.UtcNow);
                await versions.UpdateStatusAsync(version, CancellationToken.None);
            }

            if (request is not null)
            {
                await idempotencyStore.ReleaseAsync(request, CancellationToken.None);
            }

            await unitOfWork.CommitAsync(cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken);
        }
    }

    private sealed record Admission(UploadAttempt? Attempt, Guid? ReplayFileId);
}
