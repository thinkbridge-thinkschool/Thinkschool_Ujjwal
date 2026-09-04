namespace QuotesApi.Configuration;

public record ResilienceOptions
{
    public const string SectionName = "Resilience";

    // --- Day 5: zenquotes.io / IRandomQuoteClient pipeline. Unchanged. ---
    public int RetryAttempts { get; init; } = 3;
    public double CircuitBreakerFailureRatio { get; init; } = 0.5;
    public TimeSpan CircuitBreakerSamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(10);

    // Polly requires these but the brief didn't specify them; kept configurable
    // rather than hardcoded, with production-sensible defaults. RetryBaseDelay
    // in particular needs to be overridable so tests can run the real exponential
    // backoff without a multi-second test.
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(200);
    public int CircuitBreakerMinimumThroughput { get; init; } = 10;
    public TimeSpan CircuitBreakerBreakDuration { get; init; } = TimeSpan.FromSeconds(30);

    // --- Day 22: bulkhead added to the zenquotes pipeline above. ---
    // A slow zenquotes.io response must not let unbounded concurrent
    // /api/quotes/random callers pile up and exhaust the thread pool for
    // unrelated requests - this caps how many calls run at once, queuing
    // a small overflow before rejecting outright.
    public int BulkheadMaxConcurrency { get; init; } = 10;
    public int BulkheadMaxQueue { get; init; } = 5;

    // --- Day 22: second pipeline, around Redis (IDistributedCache L2,
    // introduced Day 21). Deliberately tuned differently from the
    // zenquotes numbers above - see day-22/README.md. Redis calls are
    // normally sub-millisecond, so both the per-attempt timeout and the
    // retry delay are far smaller than the HTTP pipeline's; the breaker's
    // sampling window is also shorter so the demo (and a real production
    // incident) doesn't have to wait 30s+10-samples to see it react. ---
    public int RedisRetryAttempts { get; init; } = 2;
    public TimeSpan RedisRetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(50);
    public double RedisCircuitBreakerFailureRatio { get; init; } = 0.5;
    public TimeSpan RedisCircuitBreakerSamplingDuration { get; init; } = TimeSpan.FromSeconds(10);
    public int RedisCircuitBreakerMinimumThroughput { get; init; } = 4;
    public TimeSpan RedisCircuitBreakerBreakDuration { get; init; } = TimeSpan.FromSeconds(15);
    // Per-attempt, not total - deliberately inside retry, not outside it.
    // See day-22/README.md for why this differs from the HTTP pipeline's
    // TotalTimeout.
    public TimeSpan RedisPerAttemptTimeout { get; init; } = TimeSpan.FromSeconds(1);
    public int RedisBulkheadMaxConcurrency { get; init; } = 20;
    public int RedisBulkheadMaxQueue { get; init; } = 10;
}
