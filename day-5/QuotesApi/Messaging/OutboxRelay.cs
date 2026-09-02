using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Messaging;

// Polls OutboxMessages for unprocessed rows and publishes them. Same
// scope-per-unit-of-work discipline as Days 18/19: this is a singleton
// (AddHostedService), so the constructor holds only IServiceScopeFactory
// - never a scoped QuotesDbContext/repository directly - and a fresh
// scope is created per poll batch in ProcessBatchAsync.
//
// Single-instance-only by design: two OutboxRelay instances polling the
// same table with the plain WHERE-ProcessedAt-IS-NULL query below would
// both pick up and publish the same rows. SQLite has no
// SELECT ... FOR UPDATE / row-level locking to claim rows across
// instances with, and its single-writer model means a "claim" UPDATE
// from two processes would just serialize behind each other's write
// locks rather than genuinely coordinate. Running more than one instance
// would need either a claim step (UPDATE ... WHERE ProcessedAt IS NULL
// AND ClaimedBy IS NULL, checking rows-affected before treating a row as
// claimed) against a database that actually supports atomic row-level
// claims, or moving this off polling entirely. See day-20/README.md.
public class OutboxRelay : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 20;
    private const int MaxAttempts = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelay> _logger;

    public OutboxRelay(IServiceScopeFactory scopeFactory, ILogger<OutboxRelay> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxRelay started.");

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
                // A batch-level failure (e.g. the database itself is
                // briefly unreachable) must not end this loop - same
                // "must not spin forever, but must not die either"
                // principle as the per-row handling below.
                _logger.LogError(ex, "OutboxRelay batch failed unexpectedly; will retry next poll.");
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

        _logger.LogInformation("OutboxRelay stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IQuoteEventPublisher>();

        var now = DateTimeOffset.UtcNow;

        // AttemptCount < MaxAttempts: a row that has exhausted its
        // retries stops being picked up automatically - it stays visible
        // (ProcessedAt still null, LastError explains why) rather than
        // being retried forever. That's the "must not spin forever" half
        // of the failure-handling requirement; the backoff below (via
        // NextAttemptAt) is the "must not hammer it every 5s" half.
        // The NextAttemptAt <= now half of this filter is deliberately
        // applied client-side, after materializing: the SQLite EF Core
        // provider doesn't reliably translate range comparisons
        // (<, <=, >, >=) on DateTimeOffset columns - it's stored as
        // offset-aware ISO8601 text, and ordering comparisons on that
        // aren't push-down-able the way they are for a real datetime
        // type. The ProcessedAt/AttemptCount half above IS translated to
        // SQL (equality and integer comparison, both fine), so this
        // still isn't a full table scan - just the final time check.
        var candidates = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.AttemptCount < MaxAttempts)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        var pending = candidates
            .Where(m => m.NextAttemptAt is null || m.NextAttemptAt <= now)
            .Take(BatchSize)
            .ToList();

        foreach (var row in pending)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var message = JsonSerializer.Deserialize<QuoteCreatedMessage>(row.Payload)
                    ?? throw new InvalidOperationException($"Outbox row {row.Id} has an unreadable payload.");

                await publisher.PublishAsync(message, ct);

                // Deliberately NOT atomic with the publish above - if the
                // process dies between the publish succeeding and this
                // save committing, the row is still unprocessed and gets
                // republished on the next pass. That's at-least-once by
                // design, and it's safe ONLY because Day 19's
                // AuditSubscriptionWorker/StatsSubscriptionWorker dedupe
                // on (SubscriptionName, MessageId) - a republish of the
                // same MessageId is a no-op on the consumer side, not a
                // duplicate audit row. See day-20/README.md.
                row.ProcessedAt = DateTimeOffset.UtcNow;
                row.LastError = null;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation("Outbox row {Id} published and marked sent (MessageId={MessageId}).", row.Id, row.MessageId);
            }
            catch (Exception ex)
            {
                row.AttemptCount++;
                row.LastError = ex.Message;
                row.NextAttemptAt = DateTimeOffset.UtcNow + BackoffFor(row.AttemptCount);
                await db.SaveChangesAsync(ct);

                var gaveUp = row.AttemptCount >= MaxAttempts;
                _logger.LogError(ex,
                    "Outbox row {Id} failed on attempt {Attempt}/{Max} - {Outcome}.",
                    row.Id, row.AttemptCount, MaxAttempts,
                    gaveUp ? "giving up, will not retry automatically" : $"retrying after {BackoffFor(row.AttemptCount)}");

                // Deliberately no `break`/`continue` that skips remaining
                // rows - one row's failure must not block the rows behind
                // it in this same batch.
            }
        }
    }

    // Exponential, capped: 2s, 4s, 8s, 16s, 32s (attempt 5 hits MaxAttempts
    // and stops being retried before a 6th delay would ever apply).
    private static TimeSpan BackoffFor(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));
}
