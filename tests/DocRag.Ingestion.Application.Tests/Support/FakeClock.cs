using DocRag.Ingestion.Application.Abstractions;

namespace DocRag.Ingestion.Application.Tests.Support;

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
