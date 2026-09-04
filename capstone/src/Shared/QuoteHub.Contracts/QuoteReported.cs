namespace QuoteHub.Contracts;

// Published by Curation.Application (via its own outbox) when a reader
// flags a quote. Consumed by Moderation.Application to create a queue
// entry. Async so reporting stays fast and still succeeds when Moderation
// is degraded or offline - the report must land even if nothing is
// listening yet.
public sealed record QuoteReported(
    Guid EventId,
    DateTimeOffset OccurredAt,
    int QuoteId,
    int ReportedByUserId,
    string Reason) : IIntegrationEvent;
