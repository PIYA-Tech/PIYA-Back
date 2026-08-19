using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class PharmacyStaffAuthorizationServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly PharmacyStaffService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _currentPharmacyId = Guid.NewGuid();
    private readonly Guid _expiredPharmacyId = Guid.NewGuid();

    public PharmacyStaffAuthorizationServiceTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new PharmacyApiDbContext(options);

        var user = new User
        {
            Id = _userId,
            Username = "manager",
            FirstName = "Test",
            LastName = "Manager",
            Email = "manager@example.test",
            PhoneNumber = "+994500000000",
            Role = UserRole.PharmacyManager
        };
        var currentPharmacy = CreatePharmacy(_currentPharmacyId, "Current");
        var expiredPharmacy = CreatePharmacy(_expiredPharmacyId, "Expired");
        _context.AddRange(user, currentPharmacy, expiredPharmacy);
        _context.PharmacyStaff.AddRange(
            new PharmacyStaff
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                PharmacyId = _currentPharmacyId,
                Role = PharmacyStaffRole.Manager,
                IsActive = true,
                AssignmentEndsAt = DateTime.UtcNow.AddDays(1)
            },
            new PharmacyStaff
            {
                Id = Guid.NewGuid(),
                UserId = _userId,
                PharmacyId = _expiredPharmacyId,
                Role = PharmacyStaffRole.Manager,
                IsActive = true,
                AssignmentEndsAt = DateTime.UtcNow.AddDays(-1)
            });
        _context.SaveChanges();

        _service = new PharmacyStaffService(
            _context,
            Mock.Of<IAuditService>(),
            Mock.Of<ILogger<PharmacyStaffService>>());
    }

    [Fact]
    public async Task ActiveQueries_ExcludeAssignmentsPastTheirEndTime()
    {
        var assignments = await _service.GetUserPharmaciesAsync(_userId, activeOnly: true);

        assignments.Should().ContainSingle();
        assignments[0].PharmacyId.Should().Be(_currentPharmacyId);
        (await _service.IsStaffAtPharmacyAsync(_expiredPharmacyId, _userId))
            .Should().BeFalse();
        (await _service.IsManagerAtPharmacyAsync(_expiredPharmacyId, _userId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ManagerQuery_RequiresCurrentManagerRole()
    {
        (await _service.IsManagerAtPharmacyAsync(_currentPharmacyId, _userId))
            .Should().BeTrue();

        var assignment = await _context.PharmacyStaff
            .SingleAsync(item => item.PharmacyId == _currentPharmacyId);
        assignment.Role = PharmacyStaffRole.Staff;
        await _context.SaveChangesAsync();

        (await _service.IsManagerAtPharmacyAsync(_currentPharmacyId, _userId))
            .Should().BeFalse();
    }

    [Fact]
    public async Task GetPharmacyManager_IgnoresExpiredManager()
    {
        (await _service.GetPharmacyManagerAsync(_expiredPharmacyId))
            .Should().BeNull();
        (await _service.GetPharmacyManagerAsync(_currentPharmacyId))
            .Should().NotBeNull();
    }

    private static Pharmacy CreatePharmacy(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        Country = "Azerbaijan",
        Address = $"{name} address",
        Coordinates = new Coordinates
        {
            Latitude = 40.4,
            Longitude = 49.8
        },
        Company = new PharmacyCompany
        {
            Id = Guid.NewGuid(),
            Name = $"{name} company"
        }
    };

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
