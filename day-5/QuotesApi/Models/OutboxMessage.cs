namespace QuotesApi.Models;

// Written in the SAME transaction as the Quote row it describes (see
// EndpointExtensions.cs) - the atomicity between those two writes is the
// entire point of this table. OutboxRelay (Messaging/) polls rows where
// ProcessedAt is null, publishes them, and stamps ProcessedAt - a
// separate, non-atomic step from the publish itself. See
// day-20/README.md for why that's safe.
public class OutboxMessage
{
    public int Id { get; set; }

    // The .NET type name of the payload (currently always
    // nameof(Messaging.QuoteCreatedMessage)) - not load-bearing today,
    // but means a second event type can be added later without a schema
    // change, just a second case in whatever deserializes Payload.
    public string MessageType { get; set; } = string.Empty;

    // Serialized QuoteCreatedMessage (or whatever MessageType names).
    public string Payload { get; set; } = string.Empty;

    // The SAME deterministic id QuoteCreatedPublisher derives
    // ("quote-created:{QuoteId}") - carried here so the relay can hand
    // it to the publisher unchanged, preserving Day 19's consumer-side
    // dedupe on (SubscriptionName, MessageId).
    public string MessageId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    // Null until the relay's publish call succeeds and it stamps this -
    // the query OutboxRelay polls on. Not set atomically with the
    // publish call that precedes it; see day-20/README.md.
    public DateTimeOffset? ProcessedAt { get; set; }

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    // Backoff gate: null means "eligible now." Set on failure to push the
    // next attempt out, so a failing row is retried with growing delay
    // instead of being hammered every poll - see OutboxRelay.BackoffFor.
    public DateTimeOffset? NextAttemptAt { get; set; }
}
