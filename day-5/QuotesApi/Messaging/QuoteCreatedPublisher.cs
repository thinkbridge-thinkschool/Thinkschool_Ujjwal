using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;

namespace QuotesApi.Messaging;

// Thin wrapper around a single long-lived ServiceBusSender - the SDK
// expects senders (and the client they come from) to be created once and
// reused, not created per request.
public class QuoteCreatedPublisher : IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public QuoteCreatedPublisher(ServiceBusClient client, IOptions<ServiceBusOptions> options)
    {
        _sender = client.CreateSender(options.Value.TopicName);
    }

    public async Task PublishAsync(QuoteCreatedMessage message, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var sbMessage = new ServiceBusMessage(body)
        {
            // Deterministic, derived from the quote's own id - NOT
            // Guid.NewGuid(). A quote is only ever created once, so its id
            // is already a unique, stable identity for "the QuoteCreated
            // event for this quote." A random id per send would mean a
            // retried publish looks like a brand-new message every time,
            // which defeats consumer-side dedupe entirely - see
            // day-19/README.md.
            MessageId = $"quote-created:{message.QuoteId}",
            ContentType = "application/json",
        };

        // Awaited: this is the durable hand-off to the broker, replacing
        // Day 18's in-memory TryEnqueue. What's NOT awaited is downstream
        // processing (AuditSubscriptionWorker / StatsSubscriptionWorker) -
        // that happens later, out-of-process from this call's perspective.
        await _sender.SendMessageAsync(sbMessage, ct);
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
