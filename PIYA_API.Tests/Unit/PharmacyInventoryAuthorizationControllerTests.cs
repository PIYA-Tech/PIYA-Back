using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Hubs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class PharmacyInventoryAuthorizationControllerTests
{
    private const string ReadRoles = "Pharmacist,PharmacyManager,Admin,SuperAdmin";
    private const string ManagementRoles = "PharmacyManager,Admin,SuperAdmin";
    private const string DispensingRoles = "Pharmacist,Admin,SuperAdmin";
    private const string AdministratorRoles = "Admin,SuperAdmin";

    [Theory]
    [InlineData(nameof(PharmacyInventoryController.GetPharmacyInventory), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.GetInventoryItem), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.GetBatches), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.GetExpiringBatches), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.GetStockHistory), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.GetPharmacyStockHistory), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.GetLowStockItems), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.GetExpiringItems), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.GetReorderSuggestions), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.CheckStock), ReadRoles)]
    [InlineData(nameof(PharmacyInventoryController.AddOrUpdateInventory), ManagementRoles)]
    [InlineData(nameof(PharmacyInventoryController.DeleteInventory), ManagementRoles)]
    [InlineData(nameof(PharmacyInventoryController.UpdateStock), ManagementRoles)]
    [InlineData(nameof(PharmacyInventoryController.Restock), ManagementRoles)]
    [InlineData(nameof(PharmacyInventoryController.AddBatch), ManagementRoles)]
    [InlineData(nameof(PharmacyInventoryController.DecreaseStock), DispensingRoles)]
    [InlineData(nameof(PharmacyInventoryController.RemoveExpiredBatches), AdministratorRoles)]
    public void Endpoint_UsesFinalizedRoleMatrix(string actionName, string expectedRoles)
    {
        var action = typeof(PharmacyInventoryController).GetMethod(actionName);

        action.Should().NotBeNull();
        var authorize = action!
            .GetCustomAttributes<AuthorizeAttribute>()
            .Single(attribute => attribute.Roles != null);

        authorize.Roles.Should().Be(expectedRoles);
        authorize.Roles.Should().NotContain("PharmacyNetworkOwner");
    }

    [Fact]
    public async Task GetInventoryItem_UnassignedPharmacist_IsForbidden()
    {
        var pharmacistId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetByIdAsync(inventoryId))
            .ReturnsAsync(new PharmacyInventory
            {
                Id = inventoryId,
                PharmacyId = Guid.NewGuid(),
                MedicationId = Guid.NewGuid()
            });
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetUserPharmaciesAsync(pharmacistId, true))
            .ReturnsAsync([]);
        var controller = CreateController(inventory, staff);
        SetUser(controller, pharmacistId, "Pharmacist");

        var result = await controller.GetInventoryItem(inventoryId);

        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetBatches_ExpiredAssignment_IsForbiddenBeforeBatchQuery()
    {
        var managerId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetByIdAsync(inventoryId))
            .ReturnsAsync(new PharmacyInventory
            {
                Id = inventoryId,
                PharmacyId = pharmacyId,
                MedicationId = Guid.NewGuid()
            });
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetUserPharmaciesAsync(managerId, true))
            .ReturnsAsync(
            [
                new PharmacyStaff
                {
                    PharmacyId = pharmacyId,
                    UserId = managerId,
                    IsActive = true,
                    AssignmentEndsAt = DateTime.UtcNow.AddMinutes(-1)
                }
            ]);
        var controller = CreateController(inventory, staff);
        SetUser(controller, managerId, "PharmacyManager");

        var result = await controller.GetBatches(inventoryId);

        result.Result.Should().BeOfType<ForbidResult>();
        inventory.Verify(
            service => service.GetBatchesAsync(It.IsAny<Guid>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task AddBatch_UnassignedManager_IsForbiddenBeforeMutation()
    {
        var managerId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetByIdAsync(inventoryId))
            .ReturnsAsync(new PharmacyInventory
            {
                Id = inventoryId,
                PharmacyId = Guid.NewGuid(),
                MedicationId = Guid.NewGuid()
            });
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetUserPharmaciesAsync(managerId, true))
            .ReturnsAsync([]);
        var controller = CreateController(inventory, staff);
        SetUser(controller, managerId, "PharmacyManager");

        var result = await controller.AddBatch(new BatchRequest
        {
            InventoryId = inventoryId,
            BatchNumber = "LOT-1",
            Quantity = 10
        });

        result.Result.Should().BeOfType<ForbidResult>();
        inventory.Verify(
            service => service.AddBatchAsync(It.IsAny<InventoryBatch>()),
            Times.Never);
        staff.Verify(
            service => service.IsManagerAtPharmacyAsync(
                It.IsAny<Guid>(),
                managerId),
            Times.Once);
    }

    [Fact]
    public async Task AddBatch_CurrentManager_CanMutateAssignedPharmacy()
    {
        var managerId = Guid.NewGuid();
        var pharmacyId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetByIdAsync(inventoryId))
            .ReturnsAsync(new PharmacyInventory
            {
                Id = inventoryId,
                PharmacyId = pharmacyId,
                MedicationId = Guid.NewGuid()
            });
        inventory.Setup(service => service.AddBatchAsync(It.IsAny<InventoryBatch>()))
            .ReturnsAsync((InventoryBatch batch) =>
            {
                batch.Id = Guid.NewGuid();
                return batch;
            });
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.IsManagerAtPharmacyAsync(pharmacyId, managerId))
            .ReturnsAsync(true);
        var controller = CreateController(inventory, staff);
        SetUser(controller, managerId, "PharmacyManager");

        var result = await controller.AddBatch(new BatchRequest
        {
            InventoryId = inventoryId,
            BatchNumber = "LOT-2",
            Quantity = 10
        });

        result.Result.Should().BeOfType<CreatedAtActionResult>();
        inventory.Verify(
            service => service.AddBatchAsync(It.Is<InventoryBatch>(
                batch => batch.PharmacyInventoryId == inventoryId)),
            Times.Once);
    }

    [Fact]
    public async Task GetExpiringBatches_StaffCaller_OnlyReturnsActivelyAssignedPharmacies()
    {
        var managerId = Guid.NewGuid();
        var assignedPharmacyId = Guid.NewGuid();
        var otherPharmacyId = Guid.NewGuid();
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetExpiringBatchesAsync(30))
            .ReturnsAsync(
            [
                MakeBatch(assignedPharmacyId),
                MakeBatch(otherPharmacyId)
            ]);
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetUserPharmaciesAsync(managerId, true))
            .ReturnsAsync(
            [
                new PharmacyStaff
                {
                    PharmacyId = assignedPharmacyId,
                    UserId = managerId,
                    IsActive = true
                }
            ]);
        var controller = CreateController(inventory, staff);
        SetUser(controller, managerId, "PharmacyManager");

        var result = await controller.GetExpiringBatches();

        var response = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var batches = response.Value.Should().BeAssignableTo<List<InventoryBatch>>().Subject;
        batches.Should().ContainSingle();
        batches[0].PharmacyInventory.PharmacyId.Should().Be(assignedPharmacyId);
    }

    [Fact]
    public async Task GetStockHistory_UnassignedPharmacist_IsForbiddenBeforeHistoryQuery()
    {
        var pharmacistId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetByIdAsync(inventoryId))
            .ReturnsAsync(new PharmacyInventory
            {
                Id = inventoryId,
                PharmacyId = Guid.NewGuid(),
                MedicationId = Guid.NewGuid()
            });
        var staff = new Mock<IPharmacyStaffService>();
        staff.Setup(service => service.GetUserPharmaciesAsync(pharmacistId, true))
            .ReturnsAsync([]);
        var controller = CreateController(inventory, staff);
        SetUser(controller, pharmacistId, "Pharmacist");

        var result = await controller.GetStockHistory(inventoryId);

        result.Result.Should().BeOfType<ForbidResult>();
        inventory.Verify(
            service => service.GetStockHistoryAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<InventoryTransactionType?>()),
            Times.Never);
    }

    [Fact]
    public async Task GetInventoryItem_Administrator_DoesNotRequireStaffAssignment()
    {
        var administratorId = Guid.NewGuid();
        var inventoryId = Guid.NewGuid();
        var inventory = new Mock<IInventoryService>();
        inventory.Setup(service => service.GetByIdAsync(inventoryId))
            .ReturnsAsync(new PharmacyInventory
            {
                Id = inventoryId,
                PharmacyId = Guid.NewGuid(),
                MedicationId = Guid.NewGuid()
            });
        var staff = new Mock<IPharmacyStaffService>();
        var controller = CreateController(inventory, staff);
        SetUser(controller, administratorId, "Admin");

        var result = await controller.GetInventoryItem(inventoryId);

        result.Result.Should().BeOfType<OkObjectResult>();
        staff.Verify(
            service => service.GetUserPharmaciesAsync(It.IsAny<Guid>(), It.IsAny<bool>()),
            Times.Never);
    }

    private static InventoryBatch MakeBatch(Guid pharmacyId) => new()
    {
        Id = Guid.NewGuid(),
        PharmacyInventoryId = Guid.NewGuid(),
        BatchNumber = Guid.NewGuid().ToString("N"),
        Quantity = 5,
        PharmacyInventory = new PharmacyInventory
        {
            Id = Guid.NewGuid(),
            PharmacyId = pharmacyId,
            MedicationId = Guid.NewGuid()
        }
    };

    private static PharmacyInventoryController CreateController(
        Mock<IInventoryService> inventory,
        Mock<IPharmacyStaffService> staff) =>
        new(
            inventory.Object,
            staff.Object,
            Mock.Of<IHubContext<InventoryHub>>(),
            Mock.Of<ILogger<PharmacyInventoryController>>());

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
