using DocRag.Ingestion.Application.Abstractions;

namespace DocRag.Ingestion.Application.Common;

/// <summary>System clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
