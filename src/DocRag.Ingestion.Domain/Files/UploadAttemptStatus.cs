namespace DocRag.Ingestion.Domain.Files;

/// <summary>Lifecycle of one upload attempt, tracked separately from the version's aggregate status.</summary>
public enum UploadAttemptStatus
{
    /// <summary>Bytes are being received; protected by a lease.</summary>
    Active,

    /// <summary>The attempt produced the registered original file.</summary>
    Completed,

    /// <summary>The attempt failed; its temporary object (if any) must be removed.</summary>
    Failed,

    /// <summary>The lease expired without a result (e.g. the process crashed).</summary>
    Expired,
}
