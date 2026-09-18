using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuoteHub.Contracts;

namespace QuoteHub.Moderation.Infrastructure;

// Polls moderation.OutboxMessages for unprocessed QuoteModerationDecided
// rows and dispatches each to whatever implements
// IIntegrationEventHandler<QuoteModerationDecided> - Curation's handler,
// resolved purely through that Contracts interface (see
// DependencyInjection.cs). This relay never references anything in
// QuoteHub.Curation.*; it doesn't need to know who's listening.
//
// Singleton (AddHostedService), so the constructor holds only
// IServiceScopeFactory - never a scoped ModerationDbContext - and a fresh
// DI scope is created per message, not per batch: each row's dispatch and
// its ProcessedAt update happen in one scope's lifetime, so one row's
// failure can't leave a stale DbContext behind for the next.
//
// Single-instance-only by design, same reasoning as day-5/QuotesApi's
// OutboxRelay: two instances polling the same table with this plain
// WHERE-ProcessedAt-IS-NULL query would both pick up and dispatch the
// same rows.
public sealed class ModerationOutboxRelay : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 20;

    private static readonly Meter Meter = new("QuoteHub.Moderation.OutboxRelay");

    // The one thing that makes ADR 0001's "a few seconds of staleness is
    // acceptable" claim checkable rather than asserted - see day-28's
    // self-critique. 0 when nothing is waiting.
    private static double _oldestUnprocessedRowAgeSeconds;

    private static readonly ObservableGauge<double> OldestUnprocessedRowAgeGauge = Meter.CreateObservableGauge(
        "outbox.oldest_unprocessed_row_age_seconds",
        () => _oldestUnprocessedRowAgeSeconds,
        unit: "s",
        description: "Age of the oldest unprocessed row in moderation.OutboxMessages. 0 when caught up.");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModerationOutboxRelay> _logger;

    public ModerationOutboxRelay(IServiceScopeFactory scopeFactory, ILogger<ModerationOutboxRelay> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ModerationOutboxRelay started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ModerationOutboxRelay batch failed unexpectedly; will retry next poll.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ModerationOutboxRelay stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        List<Guid> pendingIds;

        // A short-lived scope just to find the batch and update the
        // gauge - the per-row dispatch below gets its own scope each.
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();

            var pending = await db.OutboxMessages
                .Where(m => m.ProcessedAt == null)
                .OrderBy(m => m.OccurredAt)
                .Take(BatchSize)
                .ToListAsync(ct);

            _oldestUnprocessedRowAgeSeconds = pending.Count == 0
                ? 0
                : Math.Max(0, (DateTimeOffset.UtcNow - pending[0].OccurredAt).TotalSeconds);

            pendingIds = pending.Select(m => m.Id).ToList();
        }

        foreach (var id in pendingIds)
        {
            if (ct.IsCancellationRequested) break;

            await ProcessOneAsync(id, ct);
        }
    }

    private async Task ProcessOneAsync(Guid outboxMessageId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ModerationDbContext>();

        var row = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == outboxMessageId, ct);
        if (row is null || row.ProcessedAt is not null)
            return; // already handled by a previous pass

        try
        {
            if (row.EventType != nameof(QuoteModerationDecided))
            {
                _logger.LogWarning("Outbox row {Id} has unrecognised EventType {EventType}; skipping.", row.Id, row.EventType);
                return;
            }

            var integrationEvent = JsonSerializer.Deserialize<QuoteModerationDecided>(row.Payload)
                ?? throw new InvalidOperationException($"Outbox row {row.Id} has an unreadable payload.");

            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<QuoteModerationDecided>>();
            await handler.HandleAsync(integrationEvent, ct);

            row.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Outbox row {Id} dispatched and marked processed (QuoteId={QuoteId}).", row.Id, integrationEvent.QuoteId);
        }
        catch (Exception ex)
        {
            // Left unprocessed on purpose - the next poll retries it.
            // At-least-once delivery is safe here because
            // Collection.ApplyModerationDecision is idempotent: applying
            // the same visibility twice is a no-op, not a double-hide.
            _logger.LogError(ex, "Outbox row {Id} failed to dispatch; will retry next poll.", row.Id);
        }
    }
}
