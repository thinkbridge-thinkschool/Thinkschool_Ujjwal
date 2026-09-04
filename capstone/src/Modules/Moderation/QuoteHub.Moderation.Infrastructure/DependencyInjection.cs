using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuoteHub.Moderation.Application;

namespace QuoteHub.Moderation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddModerationInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<ModerationDbContext>(options =>
            options.UseSqlServer(
                config.GetConnectionString("QuoteHub")
                ?? "Server=localhost;Database=QuoteHub;Trusted_Connection=True;TrustServerCertificate=True;"));

        services.AddScoped<IModerationCaseRepository, ModerationCaseRepository>();
        services.AddScoped<QuoteReportedHandler>();

        return services;
    }
}
