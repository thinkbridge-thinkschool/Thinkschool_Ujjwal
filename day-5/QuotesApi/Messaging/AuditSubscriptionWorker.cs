using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Services;

namespace QuotesApi.Messaging;

// Competing consumer for the "audit" subscription - the Day 18 audit job,
// moved here. ServiceBusProcessor owns its own concurrency
// (MaxConcurrentCalls) and message pump internally; this class only
// starts/stops it and supplies the handler.
//
// Registered as a singleton (AddHostedService always is), so - same rule
// as Day 18's AuditLogWorker - the constructor must never take a scoped
// dependency (IQuoteRepository, QuotesDbContext, ...) directly. Only
// IServiceScopeFactory (itself singleton-safe) is held; a fresh scope is
// created per message in HandleMessageAsync.
public class AuditSubscriptionWorker : IHostedService, IAsyncDisposable
{
    private const string SubscriptionName = "audit";

    private readonly ServiceBusClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusOptions _options;
    private readonly ILogger<AuditSubscriptionWorker> _logger;
    private ServiceBusProcessor? _processor;

    public AuditSubscriptionWorker(
        ServiceBusClient client,
        IServiceScopeFactory scopeFactory,
        IOptions<ServiceBusOptions> options,
        ILogger<AuditSubscriptionWorker> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor = _client.CreateProcessor(_options.TopicName, SubscriptionName, new ServiceBusProcessorOptions
        {
            // Competing consumers: several messages processed in parallel
            // by this instance, on top of however many OTHER instances of
            // the app are also running against the same subscription -
            // Service Bus locks each message to whichever receiver picks
            // it up first, so instances compete rather than duplicate.
            MaxConcurrentCalls = 4,
            // Settled explicitly, only after the work and the dedupe row
            // are committed - see HandleMessageAsync. AutoComplete would
            // settle before that's guaranteed, which is exactly backwards.
            AutoCompleteMessages = false,
        });
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;
        await _processor.StartProcessingAsync(cancellationToken);
        _logger.LogInformation("AuditSubscriptionWorker started (subscription: {Subscription}).", SubscriptionName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
        _logger.LogInformation("AuditSubscriptionWorker stopped.");
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var payload = JsonSerializer.Deserialize<QuoteCreatedMessage>(args.Message.Body);
        if (payload is null)
        {
            _logger.LogError("Could not deserialize message {MessageId} - dead-lettering.", args.Message.MessageId);
            await args.DeadLetterMessageAsync(args.Message, "DeserializationFailed", cancellationToken: args.CancellationToken);
            return;
        }

        // The dead-letter exercise's sentinel. This is deliberately left
        // to throw, uncaught: ServiceBusProcessor abandons a message on
        // any unhandled exception from this handler, making it available
        // for redelivery, and after Config.json's MaxDeliveryCount (3)
        // attempts Service Bus automatically dead-letters it. Catching
        // this and completing anyway is exactly the mistake that silently
        // defeats the whole DLQ exercise - see day-19/README.md.
        if (payload.Poison)
        {
            throw new InvalidOperationException($"Poison message sentinel triggered for quote {payload.QuoteId}.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // Idempotency: the dedupe row and the actual work are written in
        // the SAME transaction, so they can never diverge - either both
        // commit or neither does. Checking ProcessedMessages with a plain
        // read first (not FOR UPDATE) is safe here specifically because
        // MessageId carries a database-level unique constraint too (see
        // the migration) - a genuine race between two competing
        // consumers landing on the SAME message is closed by that
        // constraint, not by this read.
        await using var tx = await db.Database.BeginTransactionAsync(args.CancellationToken);

        var alreadyProcessed = await db.ProcessedMessages.AnyAsync(
            p => p.SubscriptionName == SubscriptionName && p.MessageId == args.Message.MessageId,
            args.CancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Duplicate delivery of {MessageId} on {Subscription} (DeliveryCount={DeliveryCount}) - skipped, already processed.",
                args.Message.MessageId, SubscriptionName, args.Message.DeliveryCount);
        }
        else
        {
            db.ProcessedMessages.Add(new ProcessedMessage
            {
                SubscriptionName = SubscriptionName,
                MessageId = args.Message.MessageId,
                ProcessedAt = clock.UtcNow,
            });
            db.AuditLogs.Add(new AuditLog
            {
                QuoteId = payload.QuoteId,
                CreatedByUserId = payload.CreatedByUserId,
                CreatedAt = clock.UtcNow,
            });
            await db.SaveChangesAsync(args.CancellationToken);
            _logger.LogInformation("Audit write committed for quote {QuoteId} (MessageId={MessageId}).", payload.QuoteId, args.Message.MessageId);
        }

        await tx.CommitAsync(args.CancellationToken);

        // Settle ONLY after the transaction above has committed. If the
        // process crashes between that commit and this line, the message
        // is redelivered - and the next attempt finds the dedupe row
        // already there and takes the "alreadyProcessed" branch above
        // instead of writing a second AuditLog row. Settling any earlier
        // (or via AutoCompleteMessages) risks completing a message whose
        // work never actually committed, losing it outright on a crash.
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
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
