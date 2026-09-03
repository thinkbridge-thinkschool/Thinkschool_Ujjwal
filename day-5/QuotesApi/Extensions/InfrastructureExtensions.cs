using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
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
        // AddStackExchangeRedisCache registers IDistributedCache;
        // AddHybridCache automatically uses it as L2 and an in-memory
        // cache as L1 once both are present.
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = config.GetConnectionString("Redis") ?? "localhost:6379";
            options.InstanceName = "quotesapi:";
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