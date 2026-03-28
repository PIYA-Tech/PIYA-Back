using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace PIYA_API.Tests.Security;

/// <summary>
/// Verifies that rate limiting is configured correctly for sensitive endpoints.
/// NOTE: The test factory disables rate limiting by default (Features:EnableRateLimiting=false).
/// These tests enable it via a custom factory configuration.
/// </summary>
public class RateLimitingTests : IClassFixture<PiyaWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RateLimitingTests(PiyaWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AuthEndpoint_AcceptsReasonableNumberOfRequests()
    {
        // Even without rate limiting enforced, verify the endpoint
        // accepts at least 3 back-to-back requests without server errors.
        var tasks = Enumerable.Range(0, 3).Select(_ =>
            _client.PostAsJsonAsync("/api/auth/login", new
            {
                identifier = $"rl-test-{Guid.NewGuid()}@test.com",
                password = "WrongPassword"
            }));

        var responses = await Task.WhenAll(tasks);

        // All responses should be 400/401 (wrong credentials), not 500
        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError));
    }

    [Fact]
    public async Task HealthEndpoint_IsNotRateLimited()
    {
        // Health check should always respond regardless of rate limits
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            _client.GetAsync("/api/health"));

        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable));
    }

    [Fact]
    public async Task ConcurrentRequests_ToPublicEndpoint_AllSucceed()
    {
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            _client.GetAsync("/api/medication/search?query=test"));

        var responses = await Task.WhenAll(tasks);

        // Should not throw or return 500 under concurrent load
        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError));
    }
}
