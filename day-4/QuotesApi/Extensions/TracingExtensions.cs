using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace QuotesApi.Extensions;

public static class TracingExtensions
{
    public static WebApplicationBuilder ConfigureTracing(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("QuotesApi"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation();

                // Gated by config rather than IsDevelopment() so tests (which run under
                // Development too) can silence it via Tracing:ConsoleExporterEnabled
                // without disabling tracing itself - only the noisy console dump.
                if (builder.Configuration.GetValue<bool>("Tracing:ConsoleExporterEnabled"))
                    tracing.AddConsoleExporter();
            });

        return builder;
    }
}
