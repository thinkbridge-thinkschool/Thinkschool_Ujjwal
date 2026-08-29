using FluentAssertions;
using Quotes.Tests.Unit.TestSupport;
using QuotesApi.Models;

namespace Quotes.Tests.Unit.Models;

public class RefreshTokenTests
{
    [Fact]
    public void IsActive_UnexpiredAndUnrevoked_ReturnsTrue()
    {
        // Arrange
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var token = RefreshToken.Create("plain-token", 1, clock.UtcNow.AddMinutes(10));

        // Act
        var result = token.IsActive;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsActive_PastExpiresAt_ReturnsFalse()
    {
        // Arrange
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var token = RefreshToken.Create("plain-token", 1, clock.UtcNow.AddMinutes(-10));

        // Act
        var result = token.IsActive;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsActive_Revoked_ReturnsFalse()
    {
        // Arrange
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var token = RefreshToken.Create("plain-token", 1, clock.UtcNow.AddMinutes(10));
        token.Revoke();

        // Act
        var result = token.IsActive;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Revoke_NoReplacementProvided_SetsRevokedAt()
    {
        // Arrange
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var token = RefreshToken.Create("plain-token", 1, clock.UtcNow.AddMinutes(10));

        // Act
        token.Revoke();

        // Assert
        token.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void Revoke_WithReplacementHash_SetsReplacedByTokenHash()
    {
        // Arrange
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var token = RefreshToken.Create("plain-token", 1, clock.UtcNow.AddMinutes(10));

        // Act
        token.Revoke("replacement-hash");

        // Assert
        token.ReplacedByTokenHash.Should().Be("replacement-hash");
    }

    [Fact]
    public void Create_PlainToken_SetsTokenHashUsingHashMethod()
    {
        // Arrange
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var plainToken = "plain-token";

        // Act
        var token = RefreshToken.Create(plainToken, 1, clock.UtcNow.AddMinutes(10));

        // Assert
        token.TokenHash.Should().Be(RefreshToken.Hash(plainToken));
    }

    [Fact]
    public void Create_ValidInput_SetsUserIdAndExpiresAt()
    {
        // Arrange
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var expiresAt = clock.UtcNow.AddDays(7);

        // Act
        var token = RefreshToken.Create("plain-token", 5, expiresAt);

        // Assert
        token.UserId.Should().Be(5);
        token.ExpiresAt.Should().Be(expiresAt);
    }
}
