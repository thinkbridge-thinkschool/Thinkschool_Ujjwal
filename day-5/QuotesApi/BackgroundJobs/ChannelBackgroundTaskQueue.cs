using System.Threading.Channels;

namespace QuotesApi.BackgroundJobs;

public class ChannelBackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel;

    public ChannelBackgroundTaskQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Queue capacity must be positive.");

        // Bounded, not Channel.CreateUnbounded: an unbounded queue under
        // sustained load doesn't fail, it just grows until the process
        // OOMs - on a 1GB F1 instance that's a slow, silent way to take
        // the whole app down, not just the audit trail. FullMode.Wait
        // paired with TryWrite (never WriteAsync) below means a full
        // queue makes TryEnqueue return false immediately rather than
        // blocking - see IBackgroundTaskQueue's doc comment and
        // day-18/README.md for why the caller can never be the one
        // waiting for space.
        _channel = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public bool TryEnqueue(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return _channel.Writer.TryWrite(workItem);
    }

    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);
}
