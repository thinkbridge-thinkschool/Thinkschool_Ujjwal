using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using QuotesApi.Extensions;

namespace Quotes.Tests.Unit.Extensions;

public class AuthenticationExtensionsTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> overrides)
    {
        var full = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "unit-test-signing-key-at-least-32-bytes-long!",
            ["Jwt:Issuer"] = "QuotesApi.UnitTests",
            ["Jwt:Audience"] = "QuotesApi.UnitTests.Clients",
            ["Entra:TenantId"] = "00000000-0000-0000-0000-000000000000",
            ["Entra:Audience"] = "00000000-0000-0000-0000-000000000001"
        };

        foreach (var (key, value) in overrides)
            full[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(full).Build();
    }

    // Per-property validation (missing Key, empty Key, missing TenantId, ...) is no
    // longer this method's job - it moved to ValidateOnStart, which only fires once
    // the host actually starts (see JwtOptionsValidationTests). AddApiAuthentication
    // itself only guards against the whole section being absent, since it needs
    // concrete values immediately, before builder.Build() runs.
    [Fact]
    public void AddApiAuthentication_JwtSectionMissing_Throws()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Entra:TenantId"] = "00000000-0000-0000-0000-000000000000",
            ["Entra:Audience"] = "00000000-0000-0000-0000-000000000001"
        }).Build();
        var services = new ServiceCollection();

        var act = () => services.AddApiAuthentication(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("Jwt configuration section is missing.");
    }

    [Fact]
    public void AddApiAuthentication_EntraSectionMissing_Throws()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "unit-test-signing-key-at-least-32-bytes-long!",
            ["Jwt:Issuer"] = "QuotesApi.UnitTests",
            ["Jwt:Audience"] = "QuotesApi.UnitTests.Clients"
        }).Build();
        var services = new ServiceCollection();

        var act = () => services.AddApiAuthentication(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("Entra configuration section is missing.");
    }

    [Fact]
    public void AddApiAuthentication_AllConfigPresent_RegistersBothJwtBearerSchemes()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApiAuthentication(config);
        var provider = services.BuildServiceProvider();

        var optionsMonitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<JwtBearerOptions>>();
        optionsMonitor.Get("Internal").TokenValidationParameters.ValidIssuer.Should().Be("QuotesApi.UnitTests");
        optionsMonitor.Get("Entra").TokenValidationParameters.ValidAudiences.Should().Contain("00000000-0000-0000-0000-000000000001");
    }

    [Theory]
    [InlineData(null, "Internal")]
    [InlineData("", "Internal")]
    [InlineData("Basic dXNlcjpwYXNz", "Internal")]
    [InlineData("Bearer not-a-jwt", "Internal")]
    public void SelectScheme_NonEntraOrUnreadableHeaders_ReturnsInternal(string? header, string expected)
    {
        AuthenticationExtensions.SelectScheme(header).Should().Be(expected);
    }

    [Fact]
    public void SelectScheme_TokenWithEntraV2Issuer_ReturnsEntra()
    {
        var token = WriteUnsignedJwt(issuer: "https://login.microsoftonline.com/8d46a076-d093-416d-a57b-8692cde13bf8/v2.0");

        AuthenticationExtensions.SelectScheme($"Bearer {token}").Should().Be("Entra");
    }

    [Fact]
    public void SelectScheme_TokenWithEntraV1Issuer_ReturnsEntra()
    {
        var token = WriteUnsignedJwt(issuer: "https://sts.windows.net/8d46a076-d093-416d-a57b-8692cde13bf8/");

        AuthenticationExtensions.SelectScheme($"Bearer {token}").Should().Be("Entra");
    }

    [Fact]
    public void SelectScheme_TokenWithUnrelatedIssuer_ReturnsInternal()
    {
        var token = WriteUnsignedJwt(issuer: "QuotesApi");

        AuthenticationExtensions.SelectScheme($"Bearer {token}").Should().Be("Internal");
    }

    [Fact]
    public async Task HandleAuthenticationFailedAsync_DevelopmentEnvironment_SetsAuthErrorHeader()
    {
        var ctx = BuildFailedContext(environmentName: Environments.Development, exception: new InvalidOperationException("boom"));

        await AuthenticationExtensions.HandleAuthenticationFailedAsync(ctx);

        ctx.Response.Headers["x-auth-error"].ToString().Should().Be("InvalidOperationException");
    }

    [Fact]
    public async Task HandleAuthenticationFailedAsync_ProductionEnvironment_DoesNotSetAuthErrorHeader()
    {
        var ctx = BuildFailedContext(environmentName: Environments.Production, exception: new InvalidOperationException("boom"));

        await AuthenticationExtensions.HandleAuthenticationFailedAsync(ctx);

        ctx.Response.Headers.Should().NotContainKey("x-auth-error");
    }

    private static string WriteUnsignedJwt(string issuer)
    {
        var token = new JwtSecurityToken(issuer: issuer, audience: "any-audience");
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static AuthenticationFailedContext BuildFailedContext(string environmentName, Exception exception)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton(env);

        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        var scheme = new AuthenticationScheme("Entra", "Entra", typeof(JwtBearerHandler));

        return new AuthenticationFailedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Exception = exception
        };
    }
}
