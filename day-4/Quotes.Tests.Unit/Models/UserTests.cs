using FluentAssertions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit.Models;

public class UserTests
{
    [Fact]
    public void Create_MixedCaseEmailWithWhitespace_NormalisesAndStoresEmail()
    {
        // Arrange
        var rawEmail = "  Test@Example.COM  ";
        var password = "Password123!";

        // Act
        var user = User.Create(rawEmail, password);

        // Assert
        user.Email.Should().Be("test@example.com");
    }

    [Fact]
    public void Create_ValidPassword_StoresHashedPasswordNotPlainText()
    {
        // Arrange
        var email = "test@example.com";
        var password = "Password123!";

        // Act
        var user = User.Create(email, password);

        // Assert
        user.PasswordHash.Should().NotBe(password);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var user = User.Create("test@example.com", "Password123!");

        // Act
        var result = user.VerifyPassword("Password123!");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_IncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var user = User.Create("test@example.com", "Password123!");

        // Act
        var result = user.VerifyPassword("WrongPassword!");

        // Assert
        result.Should().BeFalse();
    }
}
