namespace QuoteHub.Moderation.Infrastructure;

// Moderation's own outbox row - deliberately a separate type from
// Curation's OutboxMessage, not a shared one. Each module publishes
// through its own outbox in its own schema; nothing here is meant to be
// shared infrastructure between modules. See day-20's OutboxRelay for the
// intended relay shape (not built here).
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
