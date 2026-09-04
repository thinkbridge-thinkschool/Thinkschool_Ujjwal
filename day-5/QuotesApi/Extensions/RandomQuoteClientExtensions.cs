using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using System.Threading.RateLimiting;
using QuotesApi.Clients;
using QuotesApi.Configuration;

namespace QuotesApi.Extensions;

public static class RandomQuoteClientExtensions
{
    public static IHttpClientBuilder AddRandomQuoteClient(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ResilienceOptions>()
            .Bind(config.GetSection(ResilienceOptions.SectionName));

        var httpClientBuilder = services.AddHttpClient<IRandomQuoteClient, RandomQuoteClient>(client =>
        {
            client.BaseAddress = new Uri("https://zenquotes.io/");
        });

        httpClientBuilder.AddResilienceHandler("default", (builder, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptions<ResilienceOptions>>().Value;
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("QuotesApi.RandomQuoteClient.Resilience");

            ConfigurePipeline(builder, options, logger);
        });

        return httpClientBuilder;
    }

    // Extracted from the AddResilienceHandler lambda so the pipeline shape itself -
    // total timeout wrapping retry wrapping circuit breaker - is unit-testable without
    // booting a host, same rationale as ShouldEnableAzureMonitorExporter/SelectScheme
    // elsewhere in this codebase.
    public static void ConfigurePipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> builder,
        ResilienceOptions options,
        ILogger logger)
    {
        // Day 22: bulkhead is outermost of all - a rejection here must not
        // even start the timeout clock or count toward the circuit
        // breaker's failure ratio, it's a distinct "we're too busy"
        // signal (see EndpointExtensions.cs's RateLimiterRejectedException
        // handling and day-22/README.md). This endpoint only ever issues
        // GET requests to zenquotes.io (RandomQuoteClient.GetRandomQuoteAsync
        // is the only caller, and it only ever calls _httpClient.GetAsync) -
        // there is no non-idempotent call this pipeline could ever retry,
        // so retry is safe here unconditionally, not just by convention.
        //
        // Total timeout is outermost of the pre-existing three so it bounds
        // the whole retry sequence's wall-clock time, not just a single
        // attempt.
        builder
            .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = options.BulkheadMaxConcurrency,
                QueueLimit = options.BulkheadMaxQueue,
            })
            .AddTimeout(options.TotalTimeout)
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                // No per-attempt timeout is configured (only the total-timeout wrapping
                // everything below), so a TimeoutRejectedException would only ever come
                // from outside this strategy - nothing for it to handle here.
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                MaxRetryAttempts = options.RetryAttempts,
                Delay = options.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Random quote request retry attempt {RetryAttempt} of {MaxRetryAttempts} after outcome {Outcome}",
                        args.AttemptNumber + 1,
                        options.RetryAttempts,
                        DescribeOutcome(args.Outcome));
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                FailureRatio = options.CircuitBreakerFailureRatio,
                SamplingDuration = options.CircuitBreakerSamplingDuration,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                BreakDuration = options.CircuitBreakerBreakDuration,
                OnOpened = args =>
                {
                    logger.LogError(
                        "Random quote client circuit breaker opened for {BreakDuration} after outcome {Outcome}",
                        args.BreakDuration,
                        DescribeOutcome(args.Outcome));
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("Random quote client circuit breaker closed");
                    return default;
                }
            });
    }

    private static string DescribeOutcome(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is not null
            ? outcome.Exception.GetType().Name
            : $"HTTP {(int)outcome.Result!.StatusCode}";
}
