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
using PIYA_API.Model;
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

    [Fact]
    public async Task CompleteTwoFactor_InvalidCode_DoesNotConsumeLoginChallenge()
    {
        var userId = Guid.NewGuid();
        var twoFactor = new Mock<ITwoFactorAuthService>();
        twoFactor.Setup(x => x.ValidateChallenge(userId, "challenge")).ReturnsAsync(true);
        twoFactor.Setup(x => x.VerifyCodeAsync(userId, "000000")).ReturnsAsync(false);
        var controller = MakeController(new Mock<IJwtService>(), twoFactor: twoFactor.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() };

        var result = await controller.CompleteLogin2FA(new Complete2FARequest
        {
            UserId = userId,
            ChallengeToken = "challenge",
            Code = "000000"
        });

        result.Should().BeOfType<UnauthorizedObjectResult>();
        twoFactor.Verify(x => x.ConsumeChallenge(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteTwoFactor_ValidCode_ConsumesChallengeBeforeIssuingSession()
    {
        var user = MakeUser();
        var users = new Mock<IUserService>();
        users.Setup(x => x.GetByIdAsync(user.Id)).ReturnsAsync(user);
        var twoFactor = new Mock<ITwoFactorAuthService>();
        twoFactor.Setup(x => x.ValidateChallenge(user.Id, "challenge")).ReturnsAsync(true);
        twoFactor.Setup(x => x.VerifyCodeAsync(user.Id, "123456")).ReturnsAsync(true);
        twoFactor.Setup(x => x.ConsumeChallenge(user.Id, "challenge")).ReturnsAsync(true);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.GenerateSecurityToken(user.Username, "iOS"))
            .ReturnsAsync(new TokenResponse
            {
                AccessToken = "access",
                RefreshToken = "refresh",
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            });
        var controller = MakeController(jwt, users.Object, twoFactor.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() };
        controller.Request.Headers[RefreshTokenTransportPolicy.ClientHeaderName] = "iOS";

        var result = await controller.CompleteLogin2FA(new Complete2FARequest
        {
            UserId = user.Id,
            ChallengeToken = "challenge",
            Code = "123456"
        });

        result.Should().BeOfType<OkObjectResult>();
        twoFactor.Verify(x => x.ConsumeChallenge(user.Id, "challenge"), Times.Once);
        jwt.Verify(x => x.GenerateSecurityToken(user.Username, "iOS"), Times.Once);
    }

    [Fact]
    public async Task Login_DeliversCodeUsingConfiguredTwoFactorMethod()
    {
        var user = MakeUser();
        var users = new Mock<IUserService>();
        users.Setup(x => x.Authenticate(user.Email, "password")).ReturnsAsync(user);
        var twoFactor = new Mock<ITwoFactorAuthService>();
        twoFactor.Setup(x => x.IsTwoFactorEnabledAsync(user.Id)).ReturnsAsync(true);
        twoFactor.Setup(x => x.IssueChallenge(user.Id)).ReturnsAsync("challenge");
        twoFactor.Setup(x => x.GetTwoFactorStatusAsync(user.Id)).ReturnsAsync(new TwoFactorAuth
        {
            UserId = user.Id,
            IsEnabled = true,
            Method = TwoFactorMethod.Email
        });
        twoFactor.Setup(x => x.SendEmailCodeAsync(user.Id)).ReturnsAsync(true);
        var security = new Mock<ISecurityHardeningService>();
        security.Setup(x => x.GetFailedLoginAttemptsAsync(user.Email, It.IsAny<TimeSpan?>()))
            .ReturnsAsync([]);
        var controller = MakeController(
            new Mock<IJwtService>(), users.Object, twoFactor.Object, security.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = MakeHttpContext() };

        var result = await controller.Login(new LoginRequest
        {
            Identifier = user.Email,
            Password = "password"
        });

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        GetProperty(ok.Value!, "twoFADelivery").Should().Be("email");
        twoFactor.Verify(x => x.SendEmailCodeAsync(user.Id), Times.Once);
        twoFactor.Verify(x => x.SendPush2FACodeAsync(It.IsAny<Guid>()), Times.Never);
    }

    private static AuthController MakeController(
        Mock<IJwtService> jwt,
        IUserService? users = null,
        ITwoFactorAuthService? twoFactor = null,
        ISecurityHardeningService? security = null) =>
        new(
            users ?? Mock.Of<IUserService>(),
            jwt.Object,
            new ConfigurationBuilder().Build(),
            Mock.Of<IAuditService>(),
            twoFactor ?? Mock.Of<ITwoFactorAuthService>(),
            security ?? Mock.Of<ISecurityHardeningService>(),
            Mock.Of<IFcmService>(),
            Mock.Of<IGdprComplianceService>(),
            Options.Create(new SecurityOptions()),
            Mock.Of<ILogger<AuthController>>());

    private static User MakeUser() => new()
    {
        Id = Guid.NewGuid(),
        Username = "two-factor-user",
        FirstName = "Two",
        LastName = "Factor",
        Email = "two-factor@example.test",
        PhoneNumber = "+994501234567"
    };

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
