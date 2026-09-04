using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using QuotesApi.Caching;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Diagnostics;
using QuotesApi.Messaging;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        // Singleton so the same interceptor instance (and its counter) is
        // handed to every QuotesDbContext created for the life of the
        // process - see DbHitCounterInterceptor's own comment for why.
        services.AddSingleton<DbHitCounterInterceptor>();
        services.AddSingleton<QuoteQueryCounter>();

        services.AddDbContext<QuotesDbContext>((sp, options) =>
            options.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=quotes.db")
                   .AddInterceptors(sp.GetRequiredService<DbHitCounterInterceptor>()));

        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // Day 19: Service Bus replaces Day 18's in-memory Channel<T>
        // queue - see day-19/README.md for why. ConnectionString comes
        // from user-secrets/environment only, never appsettings.json;
        // this repo is public.
        services.AddOptions<ServiceBusOptions>()
            .Bind(config.GetSection(ServiceBusOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ServiceBusClient (and senders created from it) are meant to be
        // long-lived and reused, not created per request - a singleton.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            return new ServiceBusClient(options.ConnectionString);
        });
        services.AddSingleton<QuoteCreatedPublisher>();
        // Same singleton instance under both the concrete type and the
        // abstraction OutboxRelay actually depends on.
        services.AddSingleton<IQuoteEventPublisher>(sp => sp.GetRequiredService<QuoteCreatedPublisher>());

        // Competing consumers: both subscriptions on the same topic,
        // each with its own ServiceBusProcessor pump.
        services.AddHostedService<AuditSubscriptionWorker>();
        services.AddHostedService<StatsSubscriptionWorker>();

        // Day 20: POST /api/quotes no longer publishes inline - it writes
        // an OutboxMessage row in the same transaction as the Quote row,
        // and this relay is what actually calls IQuoteEventPublisher,
        // asynchronously, on its own poll loop. See day-20/README.md.
        services.AddHostedService<OutboxRelay>();

        // Day 21: HybridCache, L1 (in-process) + L2 (Redis via
        // IDistributedCache). ConnectionStrings:Redis comes from
        // user-secrets/environment only, same rule as every other
        // connection-shaped value in this project - this repo is public.
        //
        // Day 22: the L2 registration is no longer AddStackExchangeRedisCache
        // (which would register the raw Redis-backed IDistributedCache
        // directly). Instead the real RedisCache is constructed by hand
        // and wrapped in ResilientDistributedCache before it's registered
        // as IDistributedCache - so HybridCache (which resolves
        // IDistributedCache for its L2) gets the resilience-wrapped
        // version transparently, with no change to HybridCache's own
        // registration or to caching behavior/keys/tags/TTLs. See
        // Caching/ResilientDistributedCache.cs and
        // Extensions/RedisResilienceExtensions.cs for the pipeline itself.
        services.AddOptions<ResilienceOptions>()
            .Bind(config.GetSection(ResilienceOptions.SectionName));

        services.AddSingleton<IDistributedCache>(sp =>
        {
            var resilienceOptions = sp.GetRequiredService<IOptions<ResilienceOptions>>().Value;

            // Found by actually running this, not assumed: Polly's
            // AddTimeout is cooperative-only in Polly v8 - it hands the
            // wrapped delegate a timeout-linked CancellationToken, but has
            // no way to forcibly abort a call that doesn't observe it.
            // StackExchange.Redis's own async calls don't reliably respect
            // an external token while a command is queued waiting for a
            // connection - measured attempts running the full ~5s Redis-
            // client-internal default timeout regardless of a 1s Polly
            // timeout wrapped around them. The real fix is making the
            // underlying client itself fail fast: ConnectTimeout/
            // SyncTimeout/AsyncTimeout all pinned to RedisPerAttemptTimeout
            // so a stuck connection gives up at the transport level, with
            // Polly's timeout remaining as a backstop for whatever that
            // doesn't catch. See day-22/README.md.
            var configOptions = StackExchange.Redis.ConfigurationOptions.Parse(
                config.GetConnectionString("Redis") ?? "localhost:6379");
            configOptions.ConnectTimeout = (int)resilienceOptions.RedisPerAttemptTimeout.TotalMilliseconds;
            configOptions.SyncTimeout = (int)resilienceOptions.RedisPerAttemptTimeout.TotalMilliseconds;
            configOptions.AsyncTimeout = (int)resilienceOptions.RedisPerAttemptTimeout.TotalMilliseconds;
            // Keep retrying to reconnect in the background rather than
            // giving up on the multiplexer permanently after one failure -
            // that's what lets calls succeed again the moment Redis comes
            // back, without needing this singleton recreated.
            configOptions.AbortOnConnectFail = false;
            configOptions.ConnectRetry = 1;

            var redisOptions = new RedisCacheOptions
            {
                ConfigurationOptions = configOptions,
                InstanceName = "quotesapi:",
            };
            var inner = new RedisCache(Microsoft.Extensions.Options.Options.Create(redisOptions));

            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("QuotesApi.Redis.Resilience");
            var pipeline = RedisResilienceExtensions.BuildPipeline(resilienceOptions, logger);

            return new ResilientDistributedCache(inner, pipeline, logger);
        });

        services.AddHybridCache(options =>
        {
            // Expiration (L2/Redis, and the ceiling for L1 too): a safety
            // net, not the primary consistency mechanism - invalidation
            // on write (RemoveByTagAsync in EndpointExtensions.cs) is
            // what actually keeps the list fresh. This just bounds how
            // stale things can get if an invalidation is ever missed.
            //
            // LocalCacheExpiration (L1/in-memory) is deliberately much
            // shorter, for a reason that has nothing to do with data
            // freshness under normal operation: RemoveByTagAsync clears
            // L2 (Redis, shared) and the calling instance's own L1, but
            // there is no cross-instance invalidation signal for OTHER
            // instances' L1 caches. In a multi-instance deployment, a
            // short local expiration is the only thing bounding how long
            // a different instance can keep serving a stale L1 hit after
            // someone else's write. On a single instance (this app's
            // actual deployment shape today) that scenario can't happen,
            // but the short TTL costs nothing and is the correct default
            // for the architecture, not just today's instance count.
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(60),
                LocalCacheExpiration = TimeSpan.FromSeconds(10),
            };
        });

        return services;
    }
}