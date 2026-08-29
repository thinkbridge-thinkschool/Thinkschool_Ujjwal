using System.Security.Claims;
using FluentAssertions;
using QuotesApi.Authorization;

namespace Quotes.Tests.Unit.Authorization;

public class ScopeClaimsTransformationTests
{
    [Fact]
    public async Task TransformAsync_SpaceSeparatedScpClaim_SplitsIntoIndividualScopeClaims()
    {
        // Arrange
        var transformation = new ScopeClaimsTransformation();
        var identity = new ClaimsIdentity(new[] { new Claim("scp", "quotes.read quotes.write") }, "Test");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await transformation.TransformAsync(principal);

        // Assert
        result.FindAll("scope").Select(c => c.Value).Should().BeEquivalentTo(new[] { "quotes.read", "quotes.write" });
    }

    [Fact]
    public async Task TransformAsync_MultipleRolesClaims_MapsEachToScopeClaim()
    {
        // Arrange
        var transformation = new ScopeClaimsTransformation();
        var claims = new[] { new Claim("roles", "Admin"), new Claim("roles", "Editor") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await transformation.TransformAsync(principal);

        // Assert
        result.FindAll("scope").Select(c => c.Value).Should().BeEquivalentTo(new[] { "Admin", "Editor" });
    }

    [Fact]
    public async Task TransformAsync_UnauthenticatedPrincipal_LeavesPrincipalUnchanged()
    {
        // Arrange
        var transformation = new ScopeClaimsTransformation();
        var identity = new ClaimsIdentity(new[] { new Claim("scp", "quotes.read") });
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await transformation.TransformAsync(principal);

        // Assert
        result.FindAll("scope").Should().BeEmpty();
    }

    [Fact]
    public async Task TransformAsync_PrincipalAlreadyHasScopeClaim_LeavesPrincipalUnchanged()
    {
        // Arrange
        var transformation = new ScopeClaimsTransformation();
        var claims = new[] { new Claim("scope", "quotes.read"), new Claim("scp", "quotes.write quotes.delete") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await transformation.TransformAsync(principal);

        // Assert
        result.FindAll("scope").Select(c => c.Value).Should().BeEquivalentTo(new[] { "quotes.read" });
    }

    [Fact]
    public async Task TransformAsync_AuthenticatedPrincipalWithNoScpOrRoles_AddsNoScopeClaims()
    {
        // Arrange
        var transformation = new ScopeClaimsTransformation();
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "user-1") }, "Test");
        var principal = new ClaimsPrincipal(identity);

        // Act
        var result = await transformation.TransformAsync(principal);

        // Assert
        result.FindAll("scope").Should().BeEmpty();
    }
}
