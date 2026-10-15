using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Application.Outbox;

/// <summary>Claims due outbox messages and publishes them with at-least-once semantics.</summary>
public sealed class OutboxDispatcher(
    IOutboxRepository outbox,
    IEventPublisher publisher,
    IClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    /// <summary>Publishes one batch. A failure of one message never blocks the others.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of messages published.</returns>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var batch = await outbox.ClaimBatchAsync(
            settings.BatchSize, TimeSpan.FromSeconds(settings.LeaseSeconds), clock.UtcNow, cancellationToken);

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                await outbox.MarkPublishedAsync(message.Id, clock.UtcNow, cancellationToken);
                IngestionTelemetry.OutboxPublished.Add(1);
                published++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RegisterFailureAsync(message, ex, settings, cancellationToken);
            }
        }

        return published;
    }

    /// <summary>Exponential backoff from 5 seconds, capped at 15 minutes.</summary>
    /// <param name="attempts">Attempts already made, including the one that just failed.</param>
    /// <returns>The delay before the next attempt.</returns>
    public static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, Math.Min(attempts - 1, 20)), MaxBackoff.TotalSeconds));

    private async Task RegisterFailureAsync(OutboxMessage message, Exception ex, OutboxOptions settings, CancellationToken cancellationToken)
    {
        var attempts = message.Attempts + 1;
        IngestionTelemetry.OutboxFailures.Add(1);

        if (attempts >= settings.MaxAttemptsBeforeAlert)
        {
            logger.LogError(
                ex, "ALERT: outbox message {EventId} failed {Attempts} times and keeps being retried", message.EventId, attempts);
        }
        else
        {
            logger.LogWarning(ex, "Outbox message {EventId} failed (attempt {Attempts}); will retry", message.EventId, attempts);
        }

        var summary = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
        await outbox.MarkFailedAsync(message.Id, summary, clock.UtcNow + Backoff(attempts), cancellationToken);
    }
}
