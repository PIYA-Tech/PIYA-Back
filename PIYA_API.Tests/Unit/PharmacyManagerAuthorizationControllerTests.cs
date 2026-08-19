using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class PharmacyManagerAuthorizationControllerTests
{
    [Fact]
    public async Task GetStaff_OrdinaryStaffAssignment_IsForbidden()
    {
        var userId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.IsManagerAtPharmacyAsync(pharmacyId, userId))
            .ReturnsAsync(false);
        var controller = CreateController(staff);
        SetUser(controller, userId, "PharmacyManager");

        var result = await controller.GetStaff(pharmacyId);

        result.Should().BeOfType<ForbidResult>();
        staff.Verify(
            service => service.GetPharmacyStaffAsync(
                It.IsAny<Guid>(),
                It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task GetStaff_CurrentManagerAssignment_CanReadManagedPharmacy()
    {
        var userId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.IsManagerAtPharmacyAsync(pharmacyId, userId))
            .ReturnsAsync(true);
        staff.Setup(service => service.GetPharmacyStaffAsync(pharmacyId, false))
            .ReturnsAsync([]);
        var controller = CreateController(staff);
        SetUser(controller, userId, "PharmacyManager");

        var result = await controller.GetStaff(pharmacyId);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetStaff_Administrator_DoesNotRequireManagerAssignment()
    {
        var userId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetPharmacyStaffAsync(pharmacyId, false))
            .ReturnsAsync([]);
        var controller = CreateController(staff);
        SetUser(controller, userId, "Admin");

        var result = await controller.GetStaff(pharmacyId);

        result.Should().BeOfType<OkObjectResult>();
        staff.Verify(
            service => service.IsManagerAtPharmacyAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>()),
            Times.Never);
    }

    [Fact]
    public async Task Dashboard_DoesNotRunSharedContextServicesConcurrently()
    {
        var pharmacyId = Guid.NewGuid();
        var activeCalls = 0;
        var maximumConcurrentCalls = 0;

        async Task<T> TrackCall<T>(T result)
        {
            var current = Interlocked.Increment(ref activeCalls);
            int observed;
            do
            {
                observed = maximumConcurrentCalls;
                if (observed >= current)
                    break;
            }
            while (Interlocked.CompareExchange(
                       ref maximumConcurrentCalls,
                       current,
                       observed) != observed);

            await Task.Delay(10);
            Interlocked.Decrement(ref activeCalls);
            return result;
        }

        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetPharmacyInventoryAsync(pharmacyId))
            .Returns(() => TrackCall(new List<PharmacyInventory>()));
        inventory.Setup(service => service.GetLowStockItemsAsync(pharmacyId))
            .Returns(() => TrackCall(new List<PharmacyInventory>()));
        inventory.Setup(service => service.GetExpiringItemsAsync(pharmacyId, 30))
            .Returns(() => TrackCall(new List<PharmacyInventory>()));
        inventory.Setup(service => service.GetReorderSuggestionsAsync(pharmacyId))
            .Returns(() => TrackCall(new Dictionary<Guid, int>()));
        inventory.Setup(service => service.GetPharmacyStockHistoryAsync(
                pharmacyId,
                It.IsAny<DateTime?>(),
                null))
            .Returns(() => TrackCall(new List<InventoryHistory>()));

        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetPharmacyStaffAsync(pharmacyId, true))
            .Returns(() => TrackCall(new List<PharmacyStaff>()));
        var prescriptions = new Mock<IPrescriptionService>();
        prescriptions.Setup(service => service.GetByPharmacyAsync(pharmacyId))
            .Returns(() => TrackCall(new List<Prescription>()));
        var controller = new PharmacyManagerController(
            Mock.Of<IPharmacyService>(),
            staff.Object,
            inventory.Object,
            prescriptions.Object,
            Mock.Of<ILogger<PharmacyManagerController>>());
        SetUser(controller, Guid.NewGuid(), "Admin");

        var result = await controller.GetDashboard(pharmacyId);

        result.Should().BeOfType<OkObjectResult>();
        maximumConcurrentCalls.Should().Be(
            1,
            "all services share one scoped EF Core DbContext");
    }

    private static PharmacyManagerController CreateController(
        Mock<IPharmacyStaffService> staff) =>
        new(
            Mock.Of<IPharmacyService>(),
            staff.Object,
            Mock.Of<IInventoryService>(),
            Mock.Of<IPrescriptionService>(),
            Mock.Of<ILogger<PharmacyManagerController>>());

    private static void SetUser(
        ControllerBase controller,
        Guid userId,
        params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }
}
