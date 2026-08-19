using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PIYA_API.Configuration;
using PIYA_API.Middleware;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class MiddlewareTrustAndRateLimitTests
{
    private static int _addressSequence;

    [Fact]
    public async Task RateLimiter_IgnoresSpoofedForwardingHeaders()
    {
        var middleware = CreateRateLimiter(globalLimit: 1);
        var remoteAddress = NextPrivateAddress();

        var first = await SendAsync(
            middleware,
            remoteAddress,
            "/api/test",
            forwardedFor: "198.51.100.10");
        var second = await SendAsync(
            middleware,
            remoteAddress,
            "/api/test",
            forwardedFor: "198.51.100.11");

        first.Should().Be(StatusCodes.Status200OK);
        second.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Theory]
    [InlineData("/api/qrvalidation/prescription/{id}/generate")]
    [InlineData("/api/prescription/{id}/generate-qr")]
    public async Task QrGenerationRoutes_UseConfiguredGenerationBucket(
        string pathTemplate)
    {
        var middleware = CreateRateLimiter(
            globalLimit: 100,
            qrGenerationLimit: 1);
        var remoteAddress = NextPrivateAddress();
        var path = pathTemplate.Replace("{id}", Guid.NewGuid().ToString());

        var first = await SendAsync(middleware, remoteAddress, path);
        var second = await SendAsync(middleware, remoteAddress, path);

        first.Should().Be(StatusCodes.Status200OK);
        second.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Theory]
    [InlineData("/api/qrvalidation/prescription/scan")]
    [InlineData("/api/qrvalidation/validate")]
    [InlineData("/api/prescription/validate-qr")]
    public async Task QrValidationRoutes_UseConfiguredValidationBucket(string path)
    {
        var middleware = CreateRateLimiter(
            globalLimit: 100,
            qrValidationLimit: 1);
        var remoteAddress = NextPrivateAddress();

        var first = await SendAsync(middleware, remoteAddress, path);
        var second = await SendAsync(middleware, remoteAddress, path);

        first.Should().Be(StatusCodes.Status200OK);
        second.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task QrBuckets_AreIsolatedFromGenericAndEachOther()
    {
        var middleware = CreateRateLimiter(
            globalLimit: 1,
            qrGenerationLimit: 1,
            qrValidationLimit: 1);
        var remoteAddress = NextPrivateAddress();

        var generic = await SendAsync(
            middleware,
            remoteAddress,
            "/api/unrelated");
        var generation = await SendAsync(
            middleware,
            remoteAddress,
            $"/api/prescription/{Guid.NewGuid()}/generate-qr");
        var validation = await SendAsync(
            middleware,
            remoteAddress,
            "/api/prescription/validate-qr");

        generic.Should().Be(StatusCodes.Status200OK);
        generation.Should().Be(StatusCodes.Status200OK);
        validation.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task AuthenticatedUsers_DoNotShareAnIpBucket()
    {
        var middleware = CreateRateLimiter(globalLimit: 1);
        var remoteAddress = NextPrivateAddress();

        var first = await SendAsync(
            middleware,
            remoteAddress,
            "/api/private",
            userId: Guid.NewGuid());
        var second = await SendAsync(
            middleware,
            remoteAddress,
            "/api/private",
            userId: Guid.NewGuid());

        first.Should().Be(StatusCodes.Status200OK);
        second.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task SecurityHardening_IgnoresSpoofedForwardingHeaders()
    {
        var remoteAddress = NextPrivateAddress();
        var security = new Mock<ISecurityHardeningService>();
        security.Setup(candidate => candidate.IsIpBlockedAsync(
                remoteAddress.ToString()))
            .ReturnsAsync(false);
        var reachedNext = false;
        var middleware = new SecurityHardeningMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            Mock.Of<ILogger<SecurityHardeningMiddleware>>());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Request.Path = "/api/private";
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.20";
        context.Request.Headers["X-Real-IP"] = "198.51.100.21";

        await middleware.InvokeAsync(context, security.Object);

        reachedNext.Should().BeTrue();
        security.Verify(
            candidate => candidate.IsIpBlockedAsync(remoteAddress.ToString()),
            Times.Once);
        security.Verify(
            candidate => candidate.IsIpBlockedAsync("198.51.100.20"),
            Times.Never);
        security.Verify(
            candidate => candidate.IsIpBlockedAsync("198.51.100.21"),
            Times.Never);
    }

    [Fact]
    public void LoginIdentifiers_AreNormalizedAndHashedForBucketKeys()
    {
        var first = RateLimitingMiddleware.HashLoginIdentifier(
            " Patient@Example.Test ");
        var second = RateLimitingMiddleware.HashLoginIdentifier(
            "patient@example.test");

        first.Should().Be(second);
        first.Should().MatchRegex("^[A-F0-9]{64}$");
        first.ToLowerInvariant().Should().NotContain("patient");
    }

    [Fact]
    public void Cleanup_PrunesRequestsOlderThanMaximumWindow()
    {
        var now = DateTime.UtcNow;
        var client = new ClientRateLimitInfo();
        client.Requests.Add(now.AddMinutes(-10));
        client.Requests.Add(now.AddSeconds(-10));

        var empty = RateLimitingMiddleware.PruneExpiredRequests(
            client,
            TimeSpan.FromMinutes(5),
            now);

        empty.Should().BeFalse();
        client.Requests.Should().ContainSingle()
            .Which.Should().Be(now.AddSeconds(-10));
    }

    [Fact]
    public async Task QrCleanup_UsesConfiguredRetentionDays()
    {
        var qrService = new Mock<IQRService>();
        qrService.Setup(candidate => candidate.CleanupExpiredTokensAsync(30))
            .ReturnsAsync(4);
        var services = new ServiceCollection();
        services.AddScoped(_ => qrService.Object);
        using var provider = services.BuildServiceProvider();
        var cleanup = new QrTokenCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SecurityOptions { QrTokenCleanupDays = 30 }),
            Mock.Of<ILogger<QrTokenCleanupService>>());

        var deleted = await cleanup.CleanupOnceAsync();

        deleted.Should().Be(4);
        qrService.Verify(
            candidate => candidate.CleanupExpiredTokensAsync(30),
            Times.Once);
    }

    private static RateLimitingMiddleware CreateRateLimiter(
        int globalLimit,
        int qrGenerationLimit = 5,
        int qrValidationLimit = 20)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:EnableRateLimiting"] = "true",
                ["RateLimiting:EnableGlobal"] = "true",
                ["RateLimiting:PermitLimit"] = globalLimit.ToString(),
                ["RateLimiting:WindowSeconds"] = "60",
                ["RateLimiting:Endpoints:QrGeneration:PermitLimit"] =
                    qrGenerationLimit.ToString(),
                ["RateLimiting:Endpoints:QrGeneration:WindowSeconds"] = "60",
                ["RateLimiting:Endpoints:QrValidation:PermitLimit"] =
                    qrValidationLimit.ToString(),
                ["RateLimiting:Endpoints:QrValidation:WindowSeconds"] = "60"
            })
            .Build();
        return new RateLimitingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            Mock.Of<ILogger<RateLimitingMiddleware>>(),
            configuration);
    }

    private static async Task<int> SendAsync(
        RateLimitingMiddleware middleware,
        IPAddress remoteAddress,
        string path,
        string? forwardedFor = null,
        Guid? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteAddress;
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        if (forwardedFor != null)
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (userId.HasValue)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        userId.Value.ToString())
                ],
                "test"));
        }

        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
    }

    private static IPAddress NextPrivateAddress()
    {
        var sequence = Interlocked.Increment(ref _addressSequence);
        return IPAddress.Parse(
            $"10.240.{sequence / 250}.{(sequence % 250) + 1}");
    }
}
