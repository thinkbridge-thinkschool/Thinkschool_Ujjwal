using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Authorization;
using QuotesApi.Models;

namespace Quotes.Tests.Unit.Authorization;

public class MustOwnQuoteHandlerTests
{
    [Fact]
    public async Task HandleAsync_OidMatchesCreatedByUserId_Succeeds()
    {
        // Arrange
        var handler = new MustOwnQuoteHandler();
        var quote = new Quote { Id = 1, Author = "Author", Text = "Text", CreatedByUserId = "user-1" };
        var identity = new ClaimsIdentity(new[] { new Claim("oid", "user-1") }, "Test");
        var context = new AuthorizationHandlerContext(new[] { new MustOwnQuoteRequirement() }, new ClaimsPrincipal(identity), quote);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_SubMatchesAndOidAbsent_Succeeds()
    {
        // Arrange
        var handler = new MustOwnQuoteHandler();
        var quote = new Quote { Id = 1, Author = "Author", Text = "Text", CreatedByUserId = "user-1" };
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "user-1") }, "Test");
        var context = new AuthorizationHandlerContext(new[] { new MustOwnQuoteRequirement() }, new ClaimsPrincipal(identity), quote);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NeitherOidNorSubMatches_DoesNotSucceed()
    {
        // Arrange
        var handler = new MustOwnQuoteHandler();
        var quote = new Quote { Id = 1, Author = "Author", Text = "Text", CreatedByUserId = "user-1" };
        var identity = new ClaimsIdentity(new[] { new Claim("oid", "user-2") }, "Test");
        var context = new AuthorizationHandlerContext(new[] { new MustOwnQuoteRequirement() }, new ClaimsPrincipal(identity), quote);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_PrincipalHasNoIdentifyingClaim_DoesNotSucceed()
    {
        // Arrange
        var handler = new MustOwnQuoteHandler();
        var quote = new Quote { Id = 1, Author = "Author", Text = "Text", CreatedByUserId = "user-1" };
        var context = new AuthorizationHandlerContext(new[] { new MustOwnQuoteRequirement() }, new ClaimsPrincipal(new ClaimsIdentity()), quote);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }
}
