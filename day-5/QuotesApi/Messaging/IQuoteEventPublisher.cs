namespace QuotesApi.Messaging;

// Exists so OutboxRelay depends on an abstraction, not the concrete
// Service Bus client - QuoteCreatedPublisher is the real, shipped
// implementation; a fake implementation can stand in for it wherever a
// live broker isn't available (verification, local dev), without
// touching the relay itself. See day-20/README.md.
public interface IQuoteEventPublisher
{
    Task PublishAsync(QuoteCreatedMessage message, CancellationToken ct);
}
