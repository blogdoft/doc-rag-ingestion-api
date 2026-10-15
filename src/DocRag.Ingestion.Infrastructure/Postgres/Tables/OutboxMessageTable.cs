using DocRag.Ingestion.Application.Outbox;

namespace DocRag.Ingestion.Infrastructure.Postgres.Tables;

/// <summary>Row shape of <c>outbox_messages</c>.</summary>
#pragma warning disable SA1313
internal sealed record OutboxMessageTable(
    long Id,
    Guid EventId,
    string EventType,
    Guid AggregateId,
    string Payload,
    DateTime OccurredAt,
    int Attempts)
{
    public static OutboxMessageTable FromDomain(OutboxMessage message) => new(
        message.Id,
        message.EventId,
        message.EventType,
        message.AggregateId,
        message.Payload,
        message.OccurredAt.UtcDateTime,
        message.Attempts);

    public OutboxMessage ToDomain() => new(
        Id, EventId, EventType, AggregateId, Payload, DocumentTable.Utc(OccurredAt), Attempts);
}
#pragma warning restore SA1313
