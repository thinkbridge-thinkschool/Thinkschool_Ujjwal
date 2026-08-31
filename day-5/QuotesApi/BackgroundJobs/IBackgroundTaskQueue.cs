namespace QuotesApi.BackgroundJobs;

// A generic, bounded work queue for jobs that must run after an HTTP
// response has already gone out. A work item receives the IServiceProvider
// of a scope created just for it (see AuditLogWorker) - never a request's
// own scope, which is disposed the instant that request finishes.
public interface IBackgroundTaskQueue
{
    // Non-blocking, always. Returns false if the queue is full instead of
    // waiting for space - the caller (an HTTP request handler) must never
    // be the one blocked making room for a background job. What to do
    // with a false result (log it, drop it, ...) is the caller's call,
    // not the queue's.
    bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> workItem);

    // Only AuditLogWorker's loop calls this. Cancelling `ct` stops the
    // wait for the NEXT item - it has no effect on a work item that's
    // already been dequeued and is running.
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct);
}
