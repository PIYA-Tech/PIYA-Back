using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PIYA_API.Configuration;
using PIYA_API.Controllers;
using PIYA_API.Security;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class AuthControllerSecurityTests
{
    [Fact]
    public void ValidateToken_DifferentBodyToken_IsRejectedWithoutRevalidatingIt()
    {
        var jwt = new Mock<IJwtService>();
        var controller = MakeController(jwt);
        SetAuthenticatedRequest(controller, Guid.NewGuid(), "validated-bearer");

        var result = controller.ValidateToken(new ValidateTokenRequest { Token = "attacker-token" });

        result.Should().BeOfType<UnauthorizedObjectResult>();
        jwt.Verify(x => x.ValidateToken(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Logout_RevokesOnlyAuthenticatedBearerAndOwnedRefreshToken()
    {
        var userId = Guid.NewGuid();
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.RevokeRefreshTokenAsync("native-refresh", userId))
            .ReturnsAsync(false);
        var controller = MakeController(jwt);
        SetAuthenticatedRequest(controller, userId, "validated-bearer");
        controller.Request.Headers[RefreshTokenTransportPolicy.ClientHeaderName] = "iOS";

        var result = await controller.Logout(new LogoutRequest
        {
            RefreshToken = "native-refresh",
            AccessToken = "forged-body-token"
        });

        result.Should().BeOfType<OkObjectResult>();
        jwt.Verify(x => x.RevokeRefreshTokenAsync("native-refresh", userId), Times.Once);
        jwt.Verify(x => x.RevokeAccessTokenAsync("validated-bearer"), Times.Once);
        jwt.Verify(x => x.RevokeAccessTokenAsync("forged-body-token"), Times.Never);
    }

    [Fact]
    public async Task BrowserRefresh_IgnoresBodyTokenAndDoesNotReturnRotatedRefreshToken()
    {
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.RefreshAccessToken("cookie-refresh"))
            .ReturnsAsync(new TokenResponse
            {
                AccessToken = "access",
                RefreshToken = "rotated-secret",
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        var controller = MakeController(jwt);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = MakeHttpContext()
        };
        controller.Request.Headers.Origin = "https://piya.life";
        controller.Request.Headers.Cookie = "piya_refresh_token=cookie-refresh";

        var result = await controller.RefreshToken(
            new RefreshTokenRequest { RefreshToken = "body-refresh" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        GetProperty(ok.Value!, "refreshToken").Should().BeNull();
        jwt.Verify(x => x.RefreshAccessToken("cookie-refresh"), Times.Once);
        jwt.Verify(x => x.RefreshAccessToken("body-refresh"), Times.Never);
    }

    [Fact]
    public async Task ExplicitNativeRefresh_ReturnsRotatedRefreshToken()
    {
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.RefreshAccessToken("native-refresh"))
            .ReturnsAsync(new TokenResponse
            {
                AccessToken = "access",
                RefreshToken = "rotated-secret",
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        var controller = MakeController(jwt);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = MakeHttpContext()
        };
        controller.Request.Headers[RefreshTokenTransportPolicy.ClientHeaderName] = "Android";

        var result = await controller.RefreshToken(
            new RefreshTokenRequest { RefreshToken = "native-refresh" });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        GetProperty(ok.Value!, "refreshToken").Should().Be("rotated-secret");
    }

    [Fact]
    public async Task ConcurrentRefresh_ReturnsConflictWithoutClearingBrowserCookie()
    {
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.RefreshAccessToken("cookie-refresh"))
            .ThrowsAsync(new ConcurrentRefreshTokenException(Guid.NewGuid()));
        var controller = MakeController(jwt);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = MakeHttpContext()
        };
        controller.Request.Headers.Cookie = "piya_refresh_token=cookie-refresh";

        var result = await controller.RefreshToken(new RefreshTokenRequest());

        result.Should().BeOfType<ConflictObjectResult>();
        controller.Response.Headers.SetCookie.ToString().Should().NotContain("expires=");
    }

    private static AuthController MakeController(Mock<IJwtService> jwt) =>
        new(
            Mock.Of<IUserService>(),
            jwt.Object,
            new ConfigurationBuilder().Build(),
            Mock.Of<IAuditService>(),
            Mock.Of<ITwoFactorAuthService>(),
            Mock.Of<ISecurityHardeningService>(),
            Mock.Of<IFcmService>(),
            Mock.Of<IGdprComplianceService>(),
            Options.Create(new SecurityOptions()),
            Mock.Of<ILogger<AuthController>>());

    private static void SetAuthenticatedRequest(
        ControllerBase controller,
        Guid userId,
        string bearer)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = MakeHttpContext()
        };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "test-user")
        ], "Test"));
        controller.Request.Headers.Authorization = $"Bearer {bearer}";
    }

    private static DefaultHttpContext MakeHttpContext()
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns("Test");
        var services = new ServiceCollection()
            .AddSingleton(environment.Object)
            .BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private static object? GetProperty(object instance, string name) =>
        instance.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
            ?.GetValue(instance);
}
