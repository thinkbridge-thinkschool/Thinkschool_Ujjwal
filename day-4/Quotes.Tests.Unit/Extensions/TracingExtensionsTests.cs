using FluentAssertions;
using QuotesApi.Configuration;
using QuotesApi.Extensions;

namespace Quotes.Tests.Unit.Extensions;

public class TracingExtensionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldEnableAzureMonitorExporter_ConnectionStringAbsent_ReturnsFalse(string? connectionString)
    {
        var options = new ApplicationInsightsOptions { ConnectionString = connectionString! };

        TracingExtensions.ShouldEnableAzureMonitorExporter(options).Should().BeFalse();
    }

    [Fact]
    public void ShouldEnableAzureMonitorExporter_ConnectionStringPresent_ReturnsTrue()
    {
        var options = new ApplicationInsightsOptions
        {
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };

        TracingExtensions.ShouldEnableAzureMonitorExporter(options).Should().BeTrue();
    }
}
