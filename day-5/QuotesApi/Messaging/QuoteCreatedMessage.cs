namespace QuotesApi.Messaging;

public record QuoteCreatedMessage
{
    public int QuoteId { get; init; }
    public string? CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }

    // Dead-letter exercise sentinel. QuoteCreatedPublisher (the real,
    // POST-/api/quotes path) never sets this true - only test tooling
    // publishing directly to the topic does. AuditSubscriptionWorker
    // throws on it deliberately; see that class for why the throw must
    // not be caught.
    public bool Poison { get; init; }
}
