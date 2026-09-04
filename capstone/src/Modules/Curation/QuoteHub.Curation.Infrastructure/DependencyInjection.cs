using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuoteHub.Curation.Application;

namespace QuoteHub.Curation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCurationInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<CurationDbContext>(options =>
            options.UseSqlServer(
                config.GetConnectionString("QuoteHub")
                ?? "Server=localhost;Database=QuoteHub;Trusted_Connection=True;TrustServerCertificate=True;"));

        services.AddScoped<ICollectionRepository, CollectionRepository>();
        services.AddScoped<QuoteModerationDecidedHandler>();

        return services;
    }
}
