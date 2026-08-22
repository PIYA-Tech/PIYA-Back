using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class PushNotificationControllerSecurityTests
{
    [Fact]
    public async Task UnregisterDevice_IsScopedToAuthenticatedOwner()
    {
        var userId = Guid.NewGuid();
        var fcm = new Mock<IFcmService>();
        fcm.Setup(service => service.UnregisterDeviceTokenAsync(userId, "device-token"))
            .ReturnsAsync(true);
        var controller = new PushNotificationController(fcm.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
                }
            }
        };

        var result = await controller.UnregisterDevice(new UnregisterDeviceRequest("device-token"));

        result.Should().BeOfType<OkObjectResult>();
        fcm.Verify(service => service.UnregisterDeviceTokenAsync(userId, "device-token"), Times.Once);
    }
}
