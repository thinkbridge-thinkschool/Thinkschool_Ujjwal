using System.Runtime.CompilerServices;

namespace Quotes.Tests.Integration;

// Every test factory in this project boots a real host, and each host runs under
// Development (appsettings.Development.json), which turns on both EF Command Debug
// logging and the OpenTelemetry console span exporter. With 39+ hosts booted across
// the run, unfiltered output from either would flood the test log - a single span
// dump alone is 15-20 lines. This silences both for the whole test process before any
// factory's WebApplication.CreateBuilder(args) runs - env vars are read into
// configuration synchronously at builder-creation time, so a module initializer
// (which fires once, on assembly load, before any test executes) reaches those reads
// reliably. Neither logging nor tracing is disabled in the app itself: only their
// noisy console sinks are silenced here. Individual factories (see LoggingTestFactory)
// can still opt back into a lower Serilog threshold for a single host via their own
// overrides.
internal static class TestLoggingConfig
{
    [ModuleInitializer]
    public static void Initialize()
    {
        Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Default", "Warning");
        Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Override__Microsoft.AspNetCore", "Warning");
        Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Override__QuotesApi", "Warning");
        Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command", "Warning");
        Environment.SetEnvironmentVariable("Tracing__ConsoleExporterEnabled", "false");
    }
}
