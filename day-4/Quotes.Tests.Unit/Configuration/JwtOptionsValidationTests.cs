using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QuotesApi.Extensions;

namespace Quotes.Tests.Unit.Configuration;

// Proves ValidateOnStart - registered inside AddApiAuthentication - genuinely fires
// when a host starts, not just that the registration compiles. Builds a real generic
// host through the production AddApiAuthentication code path (not a hand-rolled
// AddOptions call) so this is testing the actual wiring. Also replaces the coverage
// the 5 deleted per-property tests in AuthenticationExtensionsTests used to provide -
// AddApiAuthentication itself no longer rejects a single missing/empty property, since
// that now surfaces here, at the point the host actually starts.
public class JwtOptionsValidationTests
{
    private static Dictionary<string, string?> ValidConfigValues() => new()
    {
        ["Jwt:Key"] = "unit-test-signing-key-at-least-32-bytes-long!",
        ["Jwt:Issuer"] = "QuotesApi.UnitTests",
        ["Jwt:Audience"] = "QuotesApi.UnitTests.Clients",
        ["Entra:TenantId"] = "00000000-0000-0000-0000-000000000000",
        ["Entra:Audience"] = "00000000-0000-0000-0000-000000000001"
    };

    private static IHost BuildHost(Dictionary<string, string?> configValues)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.ClearProviders());
                services.AddApiAuthentication(config);
            })
            .Build();
    }

    [Fact]
    public void Start_JwtKeyMissing_ThrowsOptionsValidationException()
    {
        var values = ValidConfigValues();
        values.Remove("Jwt:Key");
        using var host = BuildHost(values);

        var act = () => host.Start();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Start_JwtKeyEmptyString_ThrowsOptionsValidationException()
    {
        var values = ValidConfigValues();
        values["Jwt:Key"] = "";
        using var host = BuildHost(values);

        var act = () => host.Start();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Start_EntraTenantIdMissing_ThrowsOptionsValidationException()
    {
        var values = ValidConfigValues();
        values.Remove("Entra:TenantId");
        using var host = BuildHost(values);

        var act = () => host.Start();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void Start_AllConfigValid_DoesNotThrow()
    {
        using var host = BuildHost(ValidConfigValues());

        var act = () => host.Start();

        act.Should().NotThrow();
    }
}
