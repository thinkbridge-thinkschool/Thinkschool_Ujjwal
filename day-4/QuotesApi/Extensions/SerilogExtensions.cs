using Serilog;
using Serilog.Core;

namespace QuotesApi.Extensions;

public static class SerilogExtensions
{
    public static WebApplicationBuilder ConfigureSerilog(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext();

            // Lets tests attach a capturing sink (see LoggingTestFactory) without the
            // production pipeline knowing tests exist.
            var testSink = services.GetService<ILogEventSink>();
            if (testSink is not null)
                configuration.WriteTo.Sink(testSink);
        });

        return builder;
    }
}
