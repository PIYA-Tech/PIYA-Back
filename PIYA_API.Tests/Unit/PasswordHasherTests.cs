using Xunit;
using FluentAssertions;
using PIYA_API.Service.Class;

namespace PIYA_API.Tests.Unit;

/// <summary>
/// Unit tests for PasswordHasher
/// </summary>
public class PasswordHasherTests
{
    private readonly PasswordHasher _passwordHasher;

    public PasswordHasherTests()
    {
        _passwordHasher = new PasswordHasher();
    }

    [Fact]
    public void HashPassword_ValidPassword_ReturnsHashedPassword()
    {
        // Arrange
        var password = "MySecurePassword123!";

        // Act
        var hash = _passwordHasher.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().NotBe(password);
        hash.Should().StartWith("$2a$"); // BCrypt prefix
    }

    [Fact]
    public void HashPassword_SamePassword_GeneratesDifferentHashes()
    {
        // Arrange
        var password = "SamePassword123";

        // Act
        var hash1 = _passwordHasher.HashPassword(password);
        var hash2 = _passwordHasher.HashPassword(password);

        // Assert
        hash1.Should().NotBe(hash2); // BCrypt uses salt, so hashes differ
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "MyPassword123!";
        var hash = _passwordHasher.HashPassword(password);

        // Act
        var result = _passwordHasher.VerifyPassword(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_IncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var password = "CorrectPassword";
        var hash = _passwordHasher.HashPassword(password);

        // Act
        var result = _passwordHasher.VerifyPassword("WrongPassword", hash);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void HashPassword_InvalidInput_ThrowsException(string? password)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _passwordHasher.HashPassword(password!));
    }

    [Fact]
    public void VerifyPassword_EmptyHash_ReturnsFalse()
    {
        // Act
        var result = _passwordHasher.VerifyPassword("password", "");

        // Assert
        result.Should().BeFalse();
    }
}
