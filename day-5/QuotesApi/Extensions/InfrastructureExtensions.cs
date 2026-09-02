using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Configuration;
using QuotesApi.Data;
using QuotesApi.Messaging;
using QuotesApi.Repositories;
using QuotesApi.Services;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<QuotesDbContext>(options =>
            options.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=quotes.db"));

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

        return services;
    }
}