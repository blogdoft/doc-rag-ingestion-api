using DocRag.Ingestion.Application.Files;
using DocRag.Ingestion.Application.Options;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Worker.Workers;

/// <summary>Periodically repairs drift between PostgreSQL and object storage.</summary>
public sealed class ReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReconciliationOptions> options,
    ILogger<ReconciliationWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["ReconciliationRunId"] = Guid.NewGuid().ToString("N") }))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<UploadReconciler>().RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Reconciliation run failed; it will be retried at the next interval");
                }
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested: the loop condition ends the worker.
            }
        }
    }
}
