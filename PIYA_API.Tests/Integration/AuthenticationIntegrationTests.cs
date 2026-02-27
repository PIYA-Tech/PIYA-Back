using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace PIYA_API.Tests.Integration;

/// <summary>
/// Integration tests for Authentication flow
/// </summary>
public class AuthenticationIntegrationTests : IClassFixture<PiyaWebApplicationFactory>
{
    private readonly PiyaWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationIntegrationTests(PiyaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Register_ValidUser_ReturnsSuccess()
    {
        // Arrange
        var registerRequest = new
        {
            email = $"test-{Guid.NewGuid()}@example.com",
            password = "Test@Password123",
            firstName = "Test",
            lastName = "User",
            phoneNumber = "+994501234567",
            dateOfBirth = "1990-01-01",
            role = "Patient"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("token");
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsBadRequest()
    {
        // Arrange
        var email = $"duplicate-{Guid.NewGuid()}@example.com";
        var registerRequest = new
        {
            email,
            password = "Test@Password123",
            firstName = "Test",
            lastName = "User",
            phoneNumber = "+994501234567",
            dateOfBirth = "1990-01-01",
            role = "Patient"
        };

        // Act - Register first time
        await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Act - Register second time with same email
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        // Arrange - First register a user
        var email = $"login-test-{Guid.NewGuid()}@example.com";
        var password = "Test@Password123";
        
        var registerRequest = new
        {
            email,
            password,
            firstName = "Test",
            lastName = "User",
            phoneNumber = "+994501234567",
            dateOfBirth = "1990-01-01",
            role = "Patient"
        };
        await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Act - Login
        var loginRequest = new { email, password };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("accessToken");
        content.Should().Contain("refreshToken");
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var loginRequest = new
        {
            email = "nonexistent@example.com",
            password = "WrongPassword123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ValidToken_ReturnsNewTokens()
    {
        // Arrange - Register and login to get tokens
        var email = $"refresh-test-{Guid.NewGuid()}@example.com";
        var password = "Test@Password123";
        
        var registerRequest = new
        {
            email,
            password,
            firstName = "Test",
            lastName = "User",
            phoneNumber = "+994501234567",
            dateOfBirth = "1990-01-01",
            role = "Patient"
        };
        await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginRequest = new { email, password };
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var loginData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(loginContent);
        var refreshToken = loginData!["refreshToken"].GetString();

        // Act - Refresh token
        var refreshRequest = new { refreshToken };
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("accessToken");
    }
}
