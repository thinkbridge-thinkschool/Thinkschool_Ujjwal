namespace QuotesApi.Models;

// Idempotency record for Service Bus subscription consumers. One publish
// fans out to every subscription on the topic independently, so the
// dedupe key is (SubscriptionName, MessageId), not MessageId alone -
// "audit" and "stats" both legitimately see the same MessageId once each;
// a MessageId-only key would let one subscription's real first delivery
// look like a duplicate of the other's. See day-19/README.md.
public class ProcessedMessage
{
    public int Id { get; set; }
    public string SubscriptionName { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}
