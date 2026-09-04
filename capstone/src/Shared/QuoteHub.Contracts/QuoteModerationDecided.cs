namespace QuoteHub.Contracts;

// Published by Moderation.Application after a decision is committed (via
// its outbox - see day-20/README.md's OutboxRelay). Consumed by
// Curation.Application to update the read-model copy each collection item
// carries (see the boundary decision in DESIGN.md). Async and fan-out on
// purpose: one quote can sit in hundreds of collections, and the moderator
// who made the call must not wait on hundreds of writes.
public sealed record QuoteModerationDecided(
    Guid EventId,
    DateTimeOffset OccurredAt,
    int QuoteId,
    ModerationDecision Decision) : IIntegrationEvent;
