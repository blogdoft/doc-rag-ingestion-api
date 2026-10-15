namespace DocRag.Ingestion.Application.Outbox;

/// <summary>A message waiting to be (or already) published to the broker.</summary>
/// <param name="Id">Internal key; 0 until stored.</param>
/// <param name="EventId">Public event identifier, the consumers' deduplication key.</param>
/// <param name="EventType">Event type name.</param>
/// <param name="AggregateId">Public identifier of the aggregate (the document).</param>
/// <param name="Payload">JSON payload.</param>
/// <param name="OccurredAt">When the fact happened.</param>
/// <param name="Attempts">Delivery attempts so far.</param>
public sealed record OutboxMessage(
    long Id,
    Guid EventId,
    string EventType,
    Guid AggregateId,
    string Payload,
    DateTimeOffset OccurredAt,
    int Attempts)
{
    /// <summary>Gets the broker partitioning key, keeping one document's events ordered.</summary>
    public string Key => AggregateId.ToString();
}

/// <summary>Outbox health figures.</summary>
/// <param name="Pending">Unpublished messages.</param>
/// <param name="OldestAge">Age of the oldest unpublished message.</param>
public sealed record OutboxBacklog(long Pending, TimeSpan OldestAge);
