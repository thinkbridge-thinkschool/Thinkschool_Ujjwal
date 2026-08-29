using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Configuration;

namespace QuotesApi.Extensions;

public static class TracingExtensions
{
    public static WebApplicationBuilder ConfigureTracing(this WebApplicationBuilder builder)
    {
        var appInsightsOptions = builder.Configuration.GetSection(ApplicationInsightsOptions.SectionName)
            .Get<ApplicationInsightsOptions>() ?? new ApplicationInsightsOptions();

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

                // The 40 integration tests each boot a host; none may attempt a network
                // call to Azure, so an absent connection string means the exporter is
                // never registered - not registered-but-disabled, never added at all.
                if (ShouldEnableAzureMonitorExporter(appInsightsOptions))
                {
                    tracing.AddAzureMonitorTraceExporter(options =>
                        options.ConnectionString = appInsightsOptions.ConnectionString);
                }
            });

        return builder;
    }

    // Extracted so the gate can be unit-tested without booting a host or risking a
    // real network call to Azure Monitor.
    public static bool ShouldEnableAzureMonitorExporter(ApplicationInsightsOptions options) =>
        !string.IsNullOrWhiteSpace(options.ConnectionString);
}
