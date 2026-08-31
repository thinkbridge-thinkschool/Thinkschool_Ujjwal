using Microsoft.EntityFrameworkCore;
using QuotesApi.BackgroundJobs;
using QuotesApi.Data;
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

        // Capacity is configurable (BackgroundQueue:Capacity) but defaults
        // to 100 - generous headroom for this app's actual load (a demo
        // API on an F1 instance) without letting a runaway burst grow
        // memory without bound. See day-18/README.md for the full
        // reasoning behind the number and the bounded-channel choice.
        var queueCapacity = config.GetValue<int?>("BackgroundQueue:Capacity") ?? 100;
        services.AddSingleton<IBackgroundTaskQueue>(new ChannelBackgroundTaskQueue(queueCapacity));
        services.AddHostedService<AuditLogWorker>();

        return services;
    }
}