using System.ComponentModel.DataAnnotations;

namespace DocRag.Ingestion.Application.Options;

/// <summary>Upload policy, bound from the <c>Upload</c> configuration section.</summary>
public sealed class UploadOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Upload";

    /// <summary>Gets or sets the maximum accepted size in bytes.</summary>
    [Range(1, long.MaxValue)]
    public long MaxBytes { get; set; } = 1_073_741_824;

    /// <summary>Gets or sets the accepted MIME types.</summary>
    [MinLength(1)]
    public string[] AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "text/markdown",
        "text/plain",
        "text/html",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ];

    /// <summary>Gets or sets how many uploads the service may run at once.</summary>
    [Range(1, 1000)]
    public int MaxConcurrentUploads { get; set; } = 3;

    /// <summary>Gets or sets the upload lease in minutes.</summary>
    [Range(1, 1440)]
    public int LeaseMinutes { get; set; } = 30;

    /// <summary>Gets or sets how long idempotency records are kept, in hours.</summary>
    [Range(1, 24 * 90)]
    public int IdempotencyTtlHours { get; set; } = 24;
}

/// <summary>Outbox dispatcher policy, bound from the <c>Outbox</c> configuration section.</summary>
public sealed class OutboxOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Outbox";

    /// <summary>Gets or sets how many messages are claimed per poll.</summary>
    [Range(1, 10000)]
    public int BatchSize { get; set; } = 100;

    /// <summary>Gets or sets the polling interval in seconds.</summary>
    [Range(1, 3600)]
    public int PollingSeconds { get; set; } = 5;

    /// <summary>Gets or sets the attempt count from which failures are raised as alerts.</summary>
    [Range(1, 1000)]
    public int MaxAttemptsBeforeAlert { get; set; } = 20;

    /// <summary>Gets or sets the processing lease of a claimed batch, in seconds.</summary>
    [Range(5, 3600)]
    public int LeaseSeconds { get; set; } = 120;
}

/// <summary>Reconciliation policy, bound from the <c>Reconciliation</c> configuration section.</summary>
public sealed class ReconciliationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Reconciliation";

    /// <summary>Gets or sets how often reconciliation runs, in seconds.</summary>
    [Range(5, 86400)]
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>Gets or sets the age after which multipart uploads count as abandoned, in minutes.</summary>
    [Range(1, 10080)]
    public int MultipartMaxAgeMinutes { get; set; } = 120;

    /// <summary>Gets or sets how many attempts are cleaned per run.</summary>
    [Range(1, 10000)]
    public int CleanupBatchSize { get; set; } = 200;
}
