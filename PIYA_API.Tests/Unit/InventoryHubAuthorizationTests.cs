using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Moq;
using PIYA_API.Hubs;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class InventoryHubAuthorizationTests
{
    [Fact]
    public void Hub_RequiresInventoryStaffRole()
    {
        var authorize = typeof(InventoryHub)
            .GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("Pharmacist,PharmacyManager,Admin,SuperAdmin");
    }

    [Fact]
    public async Task JoinPharmacy_UnassignedPharmacist_IsRejected()
    {
        var userId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.IsStaffAtPharmacyAsync(pharmacyId, userId))
            .ReturnsAsync(false);
        var (hub, groups) = CreateHub(staff, userId, "Pharmacist");

        var action = () => hub.JoinPharmacy(pharmacyId.ToString());

        await action.Should().ThrowAsync<HubException>();
        groups.Verify(
            manager => manager.AddToGroupAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task JoinPharmacy_AssignedPharmacist_JoinsCanonicalGroup()
    {
        var userId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.IsStaffAtPharmacyAsync(pharmacyId, userId))
            .ReturnsAsync(true);
        var (hub, groups) = CreateHub(staff, userId, "Pharmacist");

        await hub.JoinPharmacy(pharmacyId.ToString("B"));

        groups.Verify(
            manager => manager.AddToGroupAsync(
                "connection-1",
                $"inventory:{pharmacyId:D}",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static (InventoryHub Hub, Mock<IGroupManager> Groups) CreateHub(
        Mock<IPharmacyStaffService> staff,
        Guid userId,
        string role)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        ],
        "Test"));
        var context = new Mock<HubCallerContext>();
        context.SetupGet(value => value.ConnectionId).Returns("connection-1");
        context.SetupGet(value => value.User).Returns(principal);
        var groups = new Mock<IGroupManager>();
        groups.Setup(manager => manager.AddToGroupAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hub = new InventoryHub(staff.Object)
        {
            Context = context.Object,
            Groups = groups.Object
        };
        return (hub, groups);
    }
}
