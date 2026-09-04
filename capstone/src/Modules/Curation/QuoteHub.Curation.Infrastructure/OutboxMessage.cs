namespace QuoteHub.Curation.Infrastructure;

// Curation's own outbox row, written in the same transaction as the
// aggregate change that caused it. Publishing (deserialize, dispatch,
// mark processed) is a relay's job, shaped like day-20's OutboxRelay -
// not built here; see IIntegrationEventHandler.cs in QuoteHub.Contracts.
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
