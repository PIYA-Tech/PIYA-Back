using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
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

    [Fact]
    public async Task RegisterAndLogin_AccessTokensRehydrateSameFullProfileFromAuthMe()
    {
        await _factory.EnsureMigratedAsync();
        var identity = Guid.NewGuid();
        var email = $"auth-me-{identity:N}@example.test";
        var username = $"authme{identity:N}"[..24];
        var phoneSuffix = Math.Abs(identity.GetHashCode() % 10_000_000).ToString("D7");
        var phoneNumber = $"+99450{phoneSuffix}";
        const string password = "Test@Password123";
        var registerRequest = new
        {
            username,
            email,
            password,
            firstName = "Ayla",
            lastName = "Aliyeva",
            phoneNumber,
            dateOfBirth = "1991-06-15",
            role = "Patient"
        };

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var registerDocument = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync());
        var registeredUserId = registerDocument.RootElement.GetProperty("userId").GetGuid();
        var registrationToken = registerDocument.RootElement.GetProperty("accessToken").GetString();
        registrationToken.Should().NotBeNullOrWhiteSpace();

        var loginResponse = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { identifier = email, password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var loginDocument = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        loginDocument.RootElement.GetProperty("userId").GetGuid().Should().Be(registeredUserId);
        var loginToken = loginDocument.RootElement.GetProperty("accessToken").GetString();
        loginToken.Should().NotBeNullOrWhiteSpace();

        foreach (var accessToken in new[] { registrationToken!, loginToken! })
        {
            var me = await GetAuthMeAsync(accessToken);
            me.GetProperty("id").GetGuid().Should().Be(registeredUserId);
            me.GetProperty("userId").GetGuid().Should().Be(registeredUserId);
            me.GetProperty("username").GetString().Should().Be(username);
            me.GetProperty("email").GetString().Should().Be(email);
            me.GetProperty("firstName").GetString().Should().Be("Ayla");
            me.GetProperty("middleName").ValueKind.Should().Be(JsonValueKind.Null);
            me.GetProperty("lastName").GetString().Should().Be("Aliyeva");
            me.GetProperty("phoneNumber").GetString().Should().Be(phoneNumber);
            me.GetProperty("dateOfBirth").GetDateTime().Date.Should().Be(new DateTime(1991, 6, 15));
            me.GetProperty("role").GetString().Should().Be("Patient");
            me.GetProperty("isActive").GetBoolean().Should().BeTrue();
            me.GetProperty("isEmailVerified").GetBoolean().Should().BeFalse();
            me.GetProperty("isPhoneVerified").GetBoolean().Should().BeFalse();
        }
    }

    private async Task<JsonElement> GetAuthMeAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
