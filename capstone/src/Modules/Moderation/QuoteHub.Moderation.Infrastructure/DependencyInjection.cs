using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuoteHub.Contracts;
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
        services.AddScoped<IModerationService, ModerationService>();

        // Registered against the Contracts interface, not the concrete
        // type - CurationOutboxRelay dispatches to this handler without
        // ever naming QuoteReportedHandler or anything else in
        // Moderation.Application/Domain, which it isn't allowed to
        // reference. This is how the two modules stay decoupled while
        // still running in one process: through the shared vocabulary in
        // QuoteHub.Contracts and DI resolution, never a direct reference.
        services.AddScoped<IIntegrationEventHandler<QuoteReported>, QuoteReportedHandler>();

        services.AddHostedService<ModerationOutboxRelay>();

        return services;
    }
}
