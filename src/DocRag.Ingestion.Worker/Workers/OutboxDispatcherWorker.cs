using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Outbox;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Worker.Workers;

/// <summary>Polls the transactional outbox and publishes due messages to Kafka until shutdown.</summary>
public sealed class OutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcherWorker> logger) : BackgroundService
{
    private long _pending;
    private double _oldestAgeSeconds;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IngestionTelemetry.RegisterOutboxGauges(() => Interlocked.Read(ref _pending), () => _oldestAgeSeconds);
        var polling = TimeSpan.FromSeconds(options.Value.PollingSeconds);
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var runId = Guid.NewGuid().ToString("N");
            using (logger.BeginScope(new Dictionary<string, object> { ["OutboxRunId"] = runId }))
            {
                try
                {
                    var published = await DispatchAllDueAsync(stoppingToken);
                    consecutiveFailures = 0;
                    if (published > 0)
                    {
                        logger.LogInformation("Outbox run published {Published} message(s)", published);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    logger.LogError(ex, "Outbox run failed ({ConsecutiveFailures} consecutive); backing off", consecutiveFailures);
                }
            }

            var delay = consecutiveFailures == 0
                ? polling
                : TimeSpan.FromSeconds(Math.Min(polling.TotalSeconds * Math.Pow(2, consecutiveFailures), 60));
            await DelayAsync(delay, stoppingToken);
        }
    }

    private async Task<int> DispatchAllDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        var published = await dispatcher.DispatchBatchAsync(cancellationToken);

        var backlog = await scope.ServiceProvider.GetRequiredService<IOutboxRepository>().GetBacklogAsync(clock.UtcNow, cancellationToken);
        Interlocked.Exchange(ref _pending, backlog.Pending);
        _oldestAgeSeconds = backlog.OldestAge.TotalSeconds;
        return published;
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested: the loop condition ends the worker.
        }
    }
}
