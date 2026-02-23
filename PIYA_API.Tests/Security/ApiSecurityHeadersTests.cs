using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PIYA_API.Tests.Security;

/// <summary>
/// Tests for security headers (HSTS, CSP, X-Frame-Options, etc.)
/// </summary>
[Trait("Category", "Security")]
public class ApiSecurityHeadersTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiSecurityHeadersTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ApiResponse_ShouldIncludeSecurityHeaders()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    [Fact]
    public async Task ApiResponse_ShouldIncludeXFrameOptions()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
    }

    [Fact]
    public async Task ApiResponse_ShouldIncludeContentSecurityPolicy()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.Headers.Should().ContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task ApiResponse_ShouldIncludeStrictTransportSecurity()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.Headers.Should().ContainKey("Strict-Transport-Security");
    }

    [Fact]
    public async Task ApiResponse_ShouldNotExposeServerInfo()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.Headers.Should().NotContainKey("Server");
        response.Headers.Should().NotContainKey("X-Powered-By");
    }

    [Fact]
    public async Task ApiResponse_ShouldIncludeReferrerPolicy()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.Headers.Should().ContainKey("Referrer-Policy");
    }

    [Fact]
    public async Task ApiResponse_ShouldIncludePermissionsPolicy()
    {
        // Act
        var response = await _client.GetAsync("/api/health");

        // Assert
        response.Headers.Should().ContainKey("Permissions-Policy");
    }
}
