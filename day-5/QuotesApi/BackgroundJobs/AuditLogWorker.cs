namespace QuotesApi.BackgroundJobs;

// BackgroundService is registered as a singleton by AddHostedService, so
// this class's constructor must never take a scoped dependency directly
// (IQuoteRepository, QuotesDbContext, ...) - that's a captive dependency:
// the scoped service's lifetime gets silently stretched to match this
// singleton's, so every job for the life of the process would share ONE
// instance (a DbContext isn't thread-safe and isn't meant to live that
// long), or the container throws at startup if scope validation is on.
// IServiceScopeFactory is itself a singleton and is the only DI
// dependency held here; a fresh scope is created per work item instead,
// in ExecuteAsync.
public class AuditLogWorker : BackgroundService
{
    private readonly IBackgroundTaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogWorker> _logger;

    public AuditLogWorker(IBackgroundTaskQueue queue, IServiceScopeFactory scopeFactory, ILogger<AuditLogWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditLogWorker started.");

        while (true)
        {
            Func<IServiceProvider, CancellationToken, Task> workItem;
            try
            {
                // stoppingToken governs only whether we start waiting for
                // a NEW item. It has no bearing on the work item call
                // below, once dequeued - see that call's own comment.
                workItem = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("AuditLogWorker stopping: shutdown requested, no further items will be dequeued.");
                break;
            }

            // Deliberately NOT stoppingToken: this item has already been
            // pulled off the queue and is "in flight." If shutdown starts
            // while it's running, the write must be allowed to finish,
            // not be aborted mid-INSERT - a half-written audit row is
            // worse than a slightly slower shutdown. A fresh, independent
            // timeout still bounds it, so one genuinely stuck item can't
            // block shutdown forever regardless of what triggered it.
            using var workItemCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await workItem(scope.ServiceProvider, workItemCts.Token);
            }
            catch (Exception ex)
            {
                // Must not escape this loop. An unhandled exception here
                // would unwind ExecuteAsync and end the task it runs as -
                // a BackgroundService whose ExecuteAsync task faults just
                // stops, silently: the queue keeps filling, nothing ever
                // drains again, and nothing in the logs explains why.
                _logger.LogError(ex, "Background work item threw and was discarded; worker continues.");
            }
        }

        _logger.LogInformation("AuditLogWorker stopped.");
    }
}
