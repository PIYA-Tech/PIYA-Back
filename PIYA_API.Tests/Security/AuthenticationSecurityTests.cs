using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PIYA_API.Tests.Security;

/// <summary>
/// Factory variant that also enables per-endpoint rate limiting so
/// brute-force protection tests work correctly.
/// </summary>
public class RateLimitingWebApplicationFactory : PiyaWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:EnableRateLimiting"] = "true",
                ["RateLimiting:EnableGlobal"] = "false",
                ["RateLimiting:Endpoints:Authentication:PermitLimit"] = "3",
                ["RateLimiting:Endpoints:Authentication:WindowSeconds"] = "60",
            });
        });
    }
}

/// <summary>
/// Security tests for authentication endpoints.
/// Tests for common vulnerabilities: SQL injection, XSS, brute force, etc.
/// </summary>
[Trait("Category", "Security")]
public class AuthenticationSecurityTests(RateLimitingWebApplicationFactory factory)
    : IClassFixture<RateLimitingWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("admin' OR '1'='1")]
    [InlineData("admin'--")]
    [InlineData("admin' /*")]
    [InlineData("' OR 1=1--")]
    [InlineData("1' UNION SELECT NULL--")]
    public async Task Login_SqlInjectionAttempts_ReturnsUnauthorized(string maliciousUsername)
    {
        // Arrange
        var loginRequest = new
        {
            username = maliciousUsername,
            password = "password123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert — SQL injection must never result in a successful login (2xx).
        // Acceptable responses: 400 (validation), 401 (not found / bad creds).
        response.IsSuccessStatusCode.Should().BeFalse(
            "SQL injection payloads must not produce a successful login response");
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("<script>alert('XSS')</script>")]
    [InlineData("<img src=x onerror=alert('XSS')>")]
    [InlineData("javascript:alert('XSS')")]
    [InlineData("<iframe src='http://evil.com'>")]
    public async Task Register_XssAttempts_SanitizesInput(string maliciousInput)
    {
        // Arrange
        var registerRequest = new
        {
            email = "test@example.com",
            username = "testuser",
            password = "SecurePass123!",
            firstName = maliciousInput,
            lastName = "User",
            role = "Patient",
            dateOfBirth = "1990-01-01"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Assert - Should either reject or sanitize
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.OK,
            HttpStatusCode.Created
        );

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            content.Should().NotContain("<script>");
            content.Should().NotContain("javascript:");
        }
    }

    [Fact]
    public async Task Login_BruteForceAttempts_ShouldRateLimit()
    {
        // Arrange
        var loginRequest = new
        {
            username = "nonexistent",
            password = "wrongpassword"
        };

        var successCount = 0;
        var rateLimitedCount = 0;

        // Act - Attempt 20 rapid logins
        for (int i = 0; i < 20; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/auth/login", loginRequest);
            
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rateLimitedCount++;
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
                successCount++;
        }

        // Assert - Should eventually get rate limited
        rateLimitedCount.Should().BeGreaterThan(0, "Rate limiting should kick in after multiple failed attempts");
    }

    [Fact]
    public async Task RefreshToken_InvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        var refreshRequest = new
        {
            refreshToken = "invalid.token.here"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ExpiredToken_ReturnsUnauthorized()
    {
        // Arrange - Token that's clearly expired
        var refreshRequest = new
        {
            refreshToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjB9.invalid"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")] // Empty password
    [InlineData("123")] // Too short
    [InlineData("password")] // Common password
    [InlineData("12345678")] // All numbers
    public async Task Register_WeakPasswords_ReturnsValidationError(string weakPassword)
    {
        // Arrange
        var registerRequest = new
        {
            email = $"test{Guid.NewGuid()}@example.com",
            username = $"user{Guid.NewGuid()}",
            password = weakPassword,
            firstName = "Test",
            lastName = "User",
            role = "Patient",
            dateOfBirth = "1990-01-01"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_MissingRequiredFields_ReturnsBadRequest()
    {
        // Arrange
        var incompleteRequest = new
        {
            username = "testuser"
            // Missing email, password, etc.
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", incompleteRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("admin@example.com")]
    [InlineData("administrator")]
    [InlineData("root")]
    [InlineData("system")]
    public async Task Register_ReservedUsernames_ShouldBeHandledSecurely(string reservedName)
    {
        // Arrange
        var registerRequest = new
        {
            email = $"{reservedName}@test.com",
            username = reservedName,
            password = "SecurePass123!",
            firstName = "Test",
            lastName = "User",
            role = "SystemAdmin", // Trying to escalate privileges
            dateOfBirth = "1990-01-01"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/register", registerRequest);

        // Assert - Should either reject or not grant admin role
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(content);
            
            if (jsonDoc.RootElement.TryGetProperty("role", out var role))
            {
                role.GetString().Should().NotBe("SystemAdmin", 
                    "Self-registration should not grant admin privileges");
            }
        }
    }

    [Fact]
    public async Task ProtectedEndpoint_NoAuthHeader_ReturnsUnauthorized()
    {
        // Act - Try to access protected endpoint without auth
        var response = await _client.GetAsync("/api/user/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_MalformedToken_ReturnsUnauthorized()
    {
        // Arrange
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer malformed.token.value");

        // Act
        var response = await _client.GetAsync("/api/user/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
