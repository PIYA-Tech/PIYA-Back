using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class TwoFactorAuthorizationControllerTests
{
    [Fact]
    public async Task AnonymousBackupVerification_WithoutChallenge_DoesNotConsumeCode()
    {
        var twoFactor = new Mock<ITwoFactorAuthService>();
        var controller = MakeController(twoFactor);
        SetAnonymous(controller);

        var result = await controller.VerifyBackupCode(
            new VerifyBackupCodeRequest(Guid.NewGuid(), "12345678"));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        twoFactor.Verify(
            x => x.VerifyBackupCodeAsync(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task AnonymousCodeDelivery_WithoutChallenge_IsRejected()
    {
        var twoFactor = new Mock<ITwoFactorAuthService>();
        var controller = MakeController(twoFactor);
        SetAnonymous(controller);

        var result = await controller.SendEmailCode(new SendCodeRequest(Guid.NewGuid()));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        twoFactor.Verify(x => x.SendEmailCodeAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task AnonymousCodeDelivery_WithValidChallenge_IsAccepted()
    {
        var userId = Guid.NewGuid();
        var twoFactor = new Mock<ITwoFactorAuthService>();
        twoFactor.Setup(x => x.ValidateChallenge(userId, "challenge")).ReturnsAsync(true);
        var controller = MakeController(twoFactor);
        SetAnonymous(controller);

        var result = await controller.SendSmsCode(new SendCodeRequest(userId, "challenge"));

        result.Should().BeOfType<AcceptedResult>();
        twoFactor.Verify(x => x.SendSmsCodeAsync(userId), Times.Once);
    }

    [Fact]
    public async Task DisableTwoFactor_RequiresSuccessfulStepUp()
    {
        var userId = Guid.NewGuid();
        var twoFactor = new Mock<ITwoFactorAuthService>();
        twoFactor.Setup(x => x.VerifyCodeAsync(userId, "123456")).ReturnsAsync(false);
        var controller = MakeController(twoFactor);
        SetAuthenticated(controller, userId);

        var result = await controller.DisableTwoFactor(
            new StepUpTwoFactorRequest("123456"));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        twoFactor.Verify(x => x.DisableTwoFactorAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task ReconfigureEnabledTwoFactor_RequiresSuccessfulStepUp()
    {
        var userId = Guid.NewGuid();
        var twoFactor = new Mock<ITwoFactorAuthService>();
        twoFactor.Setup(x => x.GetTwoFactorStatusAsync(userId))
            .ReturnsAsync(new TwoFactorAuth { UserId = userId, IsEnabled = true });
        var controller = MakeController(twoFactor);
        SetAuthenticated(controller, userId);

        var result = await controller.EnableTwoFactor(
            new EnableTwoFactorRequest(TwoFactorMethod.TOTP));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        twoFactor.Verify(
            x => x.EnableTwoFactorAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorMethod>()),
            Times.Never);
    }

    private static TwoFactorAuthController MakeController(
        Mock<ITwoFactorAuthService> twoFactor) =>
        new(twoFactor.Object, Mock.Of<IAuditService>());

    private static void SetAnonymous(ControllerBase controller)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };
    }

    private static void SetAuthenticated(ControllerBase controller, Guid userId)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                    "Test"))
            }
        };
    }
}
