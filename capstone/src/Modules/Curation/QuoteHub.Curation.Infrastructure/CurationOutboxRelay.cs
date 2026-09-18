using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuoteHub.Contracts;

namespace QuoteHub.Curation.Infrastructure;

// Polls curation.OutboxMessages for unprocessed QuoteReported rows and
// dispatches each to whatever implements
// IIntegrationEventHandler<QuoteReported> - Moderation's handler,
// resolved purely through that Contracts interface (see
// DependencyInjection.cs). Same shape and reasoning as
// ModerationOutboxRelay in the other module: singleton, a DI scope per
// message, single-instance-only.
//
// Nothing in this scaffold's endpoints writes a QuoteReported row today -
// "report a quote" (day-28's build plan, item 3) is a direct, synchronous
// Moderation action, not routed through Curation's outbox - so this
// relay's table stays empty in normal operation for now. It's still
// built and tested (see the integration tests) because the relay's job
// is generic polling+dispatch, independent of whether today's endpoints
// happen to produce this particular event yet.
public sealed class CurationOutboxRelay : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private const int BatchSize = 20;

    private static readonly Meter Meter = new("QuoteHub.Curation.OutboxRelay");

    private static double _oldestUnprocessedRowAgeSeconds;

    private static readonly ObservableGauge<double> OldestUnprocessedRowAgeGauge = Meter.CreateObservableGauge(
        "outbox.oldest_unprocessed_row_age_seconds",
        () => _oldestUnprocessedRowAgeSeconds,
        unit: "s",
        description: "Age of the oldest unprocessed row in curation.OutboxMessages. 0 when caught up.");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CurationOutboxRelay> _logger;

    public CurationOutboxRelay(IServiceScopeFactory scopeFactory, ILogger<CurationOutboxRelay> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CurationOutboxRelay started.");

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
                _logger.LogError(ex, "CurationOutboxRelay batch failed unexpectedly; will retry next poll.");
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

        _logger.LogInformation("CurationOutboxRelay stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        List<Guid> pendingIds;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CurationDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<CurationDbContext>();

        var row = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == outboxMessageId, ct);
        if (row is null || row.ProcessedAt is not null)
            return;

        try
        {
            if (row.EventType != nameof(QuoteReported))
            {
                _logger.LogWarning("Outbox row {Id} has unrecognised EventType {EventType}; skipping.", row.Id, row.EventType);
                return;
            }

            var integrationEvent = JsonSerializer.Deserialize<QuoteReported>(row.Payload)
                ?? throw new InvalidOperationException($"Outbox row {row.Id} has an unreadable payload.");

            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<QuoteReported>>();
            await handler.HandleAsync(integrationEvent, ct);

            row.ProcessedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Outbox row {Id} dispatched and marked processed (QuoteId={QuoteId}).", row.Id, integrationEvent.QuoteId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbox row {Id} failed to dispatch; will retry next poll.", row.Id);
        }
    }
}
