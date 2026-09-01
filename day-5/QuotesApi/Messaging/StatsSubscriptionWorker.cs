using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;

namespace QuotesApi.Messaging;

// The second, deliberately simpler subscription on the same topic - no
// transaction, no dedupe bookkeeping, just logs and lets the SDK
// auto-complete. Exists to demonstrate that "audit" and "stats" each get
// their own fully independent copy of the same publish, not to be a
// template for a real stats pipeline: this simplicity is safe ONLY
// because logging a duplicate delivery is harmless. A consumer with real
// side effects would need the same idempotency treatment
// AuditSubscriptionWorker has.
public class StatsSubscriptionWorker : IHostedService, IAsyncDisposable
{
    private const string SubscriptionName = "stats";

    private readonly ServiceBusClient _client;
    private readonly ServiceBusOptions _options;
    private readonly ILogger<StatsSubscriptionWorker> _logger;
    private ServiceBusProcessor? _processor;

    public StatsSubscriptionWorker(ServiceBusClient client, IOptions<ServiceBusOptions> options, ILogger<StatsSubscriptionWorker> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor = _client.CreateProcessor(_options.TopicName, SubscriptionName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 4,
            // Left at its default (true) - unlike "audit," this
            // subscription has no commit-then-settle ordering to enforce.
        });
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;
        await _processor.StartProcessingAsync(cancellationToken);
        _logger.LogInformation("StatsSubscriptionWorker started (subscription: {Subscription}).", SubscriptionName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
        _logger.LogInformation("StatsSubscriptionWorker stopped.");
    }

    private Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var payload = JsonSerializer.Deserialize<QuoteCreatedMessage>(args.Message.Body);
        _logger.LogInformation("stats: quote {QuoteId} created (MessageId={MessageId}).", payload?.QuoteId, args.Message.MessageId);
        return Task.CompletedTask;
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Error on {Subscription} subscription (source: {Source}).", SubscriptionName, args.ErrorSource);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
    }
}
