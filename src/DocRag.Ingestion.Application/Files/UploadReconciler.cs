using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Application.Files;

/// <summary>Eventual-consistency repair between PostgreSQL and object storage.</summary>
public sealed class UploadReconciler(
    IUploadAttemptRepository attempts,
    IDocumentVersionRepository versions,
    IIdempotencyStore idempotencyStore,
    IObjectStorage storage,
    IClock clock,
    IOptions<ReconciliationOptions> options,
    ILogger<UploadReconciler> logger)
{
    /// <summary>
    /// Expires attempts with a dead lease, recovers versions stuck in <c>uploading</c>, deletes the
    /// temporary objects of failed/expired attempts, aborts abandoned multipart uploads and purges
    /// expired idempotency records. Every step is idempotent.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task completing when the run is done.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.UtcNow;

        var expired = await attempts.ExpireAllStaleAsync(now, cancellationToken);
        var recovered = await versions.RecoverOrphanedUploadingAsync(now, cancellationToken);

        var cleaned = 0;
        foreach (var attempt in await attempts.ListPendingCleanupAsync(settings.CleanupBatchSize, cancellationToken))
        {
            try
            {
                await storage.DeleteAsync(attempt.TemporaryObjectKey, cancellationToken);
                await attempts.MarkCleanedAsync(attempt.Id, cancellationToken);
                IngestionTelemetry.OrphansCleaned.Add(1);
                cleaned++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not clean the temporary object of attempt {AttemptId}", attempt.PublicId);
            }
        }

        var aborted = await storage.AbortStaleMultipartUploadsAsync(now.AddMinutes(-settings.MultipartMaxAgeMinutes), cancellationToken);
        var purged = await idempotencyStore.DeleteExpiredAsync(now, cancellationToken);

        logger.LogInformation(
            "Reconciliation finished: {Expired} attempts expired, {Recovered} versions recovered, {Cleaned} objects cleaned, {Aborted} multipart uploads aborted, {Purged} idempotency records purged",
            expired, recovered, cleaned, aborted, purged);
    }
}
