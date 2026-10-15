using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocRag.Ingestion.Application.Common;

/// <summary>Central <see cref="ActivitySource"/> and <see cref="Meter"/> of the ingestion service.</summary>
public static class IngestionTelemetry
{
    /// <summary>Name of both the activity source and the meter.</summary>
    public const string Name = "DocRag.Ingestion";

    /// <summary>Gets the activity source used for storage and broker spans.</summary>
    public static readonly ActivitySource Activities = new(Name);

    private static readonly Meter Meter = new(Name);

    /// <summary>Gets the counter of finished uploads, tagged by outcome.</summary>
    public static readonly Counter<long> UploadsTotal = Meter.CreateCounter<long>("docrag.uploads.total");

    /// <summary>Gets the counter of failed uploads, tagged by reason.</summary>
    public static readonly Counter<long> UploadFailures = Meter.CreateCounter<long>("docrag.uploads.failures");

    /// <summary>Gets the gauge-like counter of uploads in flight.</summary>
    public static readonly UpDownCounter<long> ActiveUploads = Meter.CreateUpDownCounter<long>("docrag.uploads.active");

    /// <summary>Gets the histogram of uploaded sizes in bytes.</summary>
    public static readonly Histogram<long> UploadBytes = Meter.CreateHistogram<long>("docrag.uploads.bytes", "By");

    /// <summary>Gets the histogram of upload durations in seconds.</summary>
    public static readonly Histogram<double> UploadDuration = Meter.CreateHistogram<double>("docrag.uploads.duration", "s");

    /// <summary>Gets the counter of outbox messages delivered to the broker.</summary>
    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("docrag.outbox.published");

    /// <summary>Gets the counter of failed outbox deliveries (retries).</summary>
    public static readonly Counter<long> OutboxFailures = Meter.CreateCounter<long>("docrag.outbox.failures");

    /// <summary>Gets the counter of orphaned temporary objects removed.</summary>
    public static readonly Counter<long> OrphansCleaned = Meter.CreateCounter<long>("docrag.reconciliation.orphans_cleaned");

    /// <summary>Registers observable gauges reading the outbox backlog.</summary>
    /// <param name="pending">Callback returning unpublished messages.</param>
    /// <param name="oldestAgeSeconds">Callback returning the oldest message age in seconds.</param>
    public static void RegisterOutboxGauges(Func<long> pending, Func<double> oldestAgeSeconds)
    {
        Meter.CreateObservableGauge("docrag.outbox.pending", pending);
        Meter.CreateObservableGauge("docrag.outbox.oldest_age", oldestAgeSeconds, "s");
    }
}
