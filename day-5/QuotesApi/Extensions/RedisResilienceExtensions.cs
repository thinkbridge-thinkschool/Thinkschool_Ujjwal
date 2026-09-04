using System.Threading.RateLimiting;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using QuotesApi.Configuration;

namespace QuotesApi.Extensions;

public static class RedisResilienceExtensions
{
    // Extracted from the DI registration lambda so the pipeline shape is
    // unit-testable without booting a host - same rationale as
    // RandomQuoteClientExtensions.ConfigurePipeline.
    //
    // Composition, outermost to innermost:
    //
    //   bulkhead -> circuit breaker -> retry -> timeout
    //
    // This is the OPPOSITE nesting of the zenquotes pipeline (which wraps
    // timeout(total) -> retry -> circuit breaker), and deliberately so:
    //
    // - Circuit breaker OUTSIDE retry means that once the breaker is
    //   open, a call is rejected immediately with BrokenCircuitException
    //   and NEVER enters the retry/timeout sub-pipeline at all - no
    //   attempt, no delay, no per-attempt timeout wait. That is what
    //   "the breaker should make degradation fast rather than slow"
    //   requires: a request hitting Redis while the breaker is open must
    //   fail in microseconds, not in however long a doomed retry sequence
    //   would take. (Contrast the zenquotes pipeline, where retry wraps
    //   the breaker: there, an open breaker is discovered only once
    //   retry's ShouldHandle sees BrokenCircuitException and declines to
    //   retry it - functionally similar for that pipeline's purposes, but
    //   it still means one retry-tier code path runs first.)
    // - Timeout is PER-ATTEMPT (innermost), not total, unlike the
    //   zenquotes pipeline's outermost TotalTimeout. Redis operations on
    //   the hot path of every cacheable request (GET /api/quotes) are
    //   expected to complete in low single-digit milliseconds; a single
    //   attempt that blows past RedisPerAttemptTimeout (1s default) is
    //   itself the failure signal worth reacting to immediately - and
    //   because the breaker is outside retry, a slow-but-not-dead Redis
    //   still can't make one logical cache operation block for
    //   attempts x delay x total-timeout the way a TotalTimeout scheme
    //   would risk. A total timeout here would mean a struggling-but-
    //   technically-alive Redis could still make every cache access wait
    //   out the full budget before giving up, which is exactly the "slow
    //   dependency starves everything else" failure mode the bulkhead
    //   requirement (item 1) exists to prevent - per-attempt timeout
    //   keeps each individual wait bounded tightly instead, in principle.
    //
    // A finding from actually testing this, not assumed: AddTimeout in
    // Polly v8 is COOPERATIVE ONLY - it hands the wrapped delegate a
    // timeout-linked CancellationToken and has no mechanism to forcibly
    // abort a call that doesn't observe it. Measured directly: with
    // StackExchange.Redis's own ConnectTimeout/SyncTimeout/AsyncTimeout
    // deliberately loosened to 10s and this timeout left at 1s, a call
    // against a stopped Redis took 33 SECONDS end to end and the outcome
    // was RedisConnectionException every time - never once
    // TimeoutRejectedException. This strategy has observably ZERO effect
    // against a StackExchange.Redis call stuck waiting for a connection
    // in its internal backlog; it isn't "usually loses the race," it does
    // not race at all in that failure mode. What actually bounds attempt
    // duration is InfrastructureExtensions.cs pinning the Redis client's
    // OWN ConnectTimeout/SyncTimeout/AsyncTimeout to RedisPerAttemptTimeout
    // - confirmed by the same experiment reverted (~5s per full
    // retry-wrapped call, down from the unbounded-until-Redis's-own-
    // default-5s-times-3-attempts case). This AddTimeout call is left in
    // the pipeline regardless - it's still a real backstop for a failure
    // mode that DOES honor cancellation (a slow application-level
    // deserialization step, for instance), just not the one this task's
    // live verification actually exercised. Don't remove it and don't
    // trust it alone.
    public static ResiliencePipeline BuildPipeline(ResilienceOptions options, ILogger logger)
    {
        var builder = new ResiliencePipelineBuilder();

        builder
            .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = options.RedisBulkheadMaxConcurrency,
                QueueLimit = options.RedisBulkheadMaxQueue,
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                FailureRatio = options.RedisCircuitBreakerFailureRatio,
                SamplingDuration = options.RedisCircuitBreakerSamplingDuration,
                MinimumThroughput = options.RedisCircuitBreakerMinimumThroughput,
                BreakDuration = options.RedisCircuitBreakerBreakDuration,
                OnOpened = args =>
                {
                    logger.LogError(
                        "Redis circuit breaker CLOSED -> OPEN for {BreakDuration} after outcome {Outcome}",
                        args.BreakDuration,
                        DescribeOutcome(args.Outcome));
                    return default;
                },
                OnClosed = args =>
                {
                    logger.LogInformation("Redis circuit breaker HALF-OPEN -> CLOSED (probe succeeded)");
                    return default;
                },
                OnHalfOpened = args =>
                {
                    logger.LogWarning("Redis circuit breaker OPEN -> HALF-OPEN (allowing one probe call through)");
                    return default;
                },
            })
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = options.RedisRetryAttempts,
                Delay = options.RedisRetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Redis operation retry attempt {RetryAttempt} of {MaxRetryAttempts} after outcome {Outcome}, delay {Delay}",
                        args.AttemptNumber + 1,
                        options.RedisRetryAttempts,
                        DescribeOutcome(args.Outcome),
                        args.RetryDelay);
                    return default;
                }
            })
            .AddTimeout(options.RedisPerAttemptTimeout);

        return builder.Build();
    }

    private static string DescribeOutcome(Outcome<object> outcome) =>
        outcome.Exception?.GetType().Name ?? "success";
}
