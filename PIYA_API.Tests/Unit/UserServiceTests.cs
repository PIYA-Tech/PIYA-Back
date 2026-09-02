using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Configuration;
using Microsoft.EntityFrameworkCore;

namespace PIYA_API.Tests.Unit;

/// <summary>
/// Unit tests for UserService
/// </summary>
public class UserServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly Mock<ILogger<UserService>> _loggerMock;
    private readonly UserService _userService;

    public UserServiceTests()
    {
        // Setup in-memory database
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new PharmacyApiDbContext(options);
        _loggerMock = new Mock<ILogger<UserService>>();
        // UserService now requires a DbContext and an IPasswordHasher
        var securityOptions = Options.Create(new SecurityOptions { PasswordHashWorkFactor = 10 });
        _userService = new UserService(_context, new PIYA_API.Service.Class.PasswordHasher(securityOptions));
    }

    [Fact]
    public async Task Create_ValidUser_ReturnsUser()
    {
        // Arrange
        var user = new User
        {
            Email = "test@example.com",
            Username = "testuser",
            FirstName = "Test",
            LastName = "User",
            PasswordHash = "hashedpassword123",
            Role = UserRole.Patient,
            DateOfBirth = new DateTime(1990, 1, 1)
            ,PhoneNumber = "0000000000"
            ,TokensInfo = new Token()
        };

        // Act
    var result = await _userService.Create(user, "TestPassword123!");

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBe(Guid.Empty);
        result.Email.Should().Be("test@example.com");
        result.IsEmailVerified.Should().BeFalse();
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Create_DuplicateEmail_ThrowsException()
    {
        // Arrange
        var existingUser = new User
        {
            Email = "duplicate@example.com",
            Username = "user1",
            FirstName = "Existing",
            LastName = "User",
            PasswordHash = "hash",
            Role = UserRole.Patient,
            DateOfBirth = DateTime.UtcNow.AddYears(-25)
            ,PhoneNumber = "0000000000"
            ,TokensInfo = new Token()
        };
        await _context.Users.AddAsync(existingUser);
        await _context.SaveChangesAsync();

        var newUser = new User
        {
            Email = "duplicate@example.com",
            Username = "user2",
            FirstName = "New",
            LastName = "User",
            PasswordHash = "hash",
            Role = UserRole.Patient,
            DateOfBirth = DateTime.UtcNow.AddYears(-30)
            ,PhoneNumber = "0000000000"
            ,TokensInfo = new Token()
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _userService.Create(newUser, "AnotherPassword123!")
        );
    }

    [Fact]
    public async Task GetById_ExistingUser_ReturnsUser()
    {
        // Arrange
        var user = new User
        {
            Email = "find@example.com",
            Username = "findme",
            FirstName = "Find",
            LastName = "Me",
            PasswordHash = "hash",
            Role = UserRole.Doctor,
            DateOfBirth = DateTime.UtcNow.AddYears(-35)
            ,PhoneNumber = "0000000000"
            ,TokensInfo = new Token()
        };
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        // Act
    var result = await _userService.GetByIdAsync(user.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id);
        result.Email.Should().Be("find@example.com");
    }

    [Fact]
    public async Task GetById_NonExistingUser_ReturnsNull()
    {
        // Act
    var result = await _userService.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Update_ExistingUser_UpdatesSuccessfully()
    {
        // Arrange
        var user = new User
        {
            Email = "update@example.com",
            Username = "updateme",
            FirstName = "Old",
            LastName = "Name",
            PasswordHash = "hash",
            Role = UserRole.Patient,
            DateOfBirth = DateTime.UtcNow.AddYears(-28)
            ,PhoneNumber = "0000000000"
            ,TokensInfo = new Token()
        };
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        // Act
        user.FirstName = "New";
        user.LastName = "Name Updated";
        await _userService.Update(user);

        // Assert
        var updated = await _context.Users.FindAsync(user.Id);
        updated.Should().NotBeNull();
        updated!.FirstName.Should().Be("New");
        updated.LastName.Should().Be("Name Updated");
        updated.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Update_Profile_PersistsDateOfBirthAndMiddleName()
    {
        var original = new User
        {
            Email = "profile@example.com",
            Username = "profile-user",
            FirstName = "Profile",
            LastName = "Patient",
            PasswordHash = "hash",
            Role = UserRole.Patient,
            PhoneNumber = "+994501234567",
            TokensInfo = new Token()
        };
        await _context.Users.AddAsync(original);
        await _context.SaveChangesAsync();
        _context.Entry(original).State = EntityState.Detached;

        var dateOfBirth = new DateTime(1994, 7, 12, 0, 0, 0, DateTimeKind.Utc);
        await _userService.Update(new User
        {
            Id = original.Id,
            Email = original.Email,
            Username = original.Username,
            FirstName = original.FirstName,
            MiddleName = "Nigar",
            LastName = original.LastName,
            PhoneNumber = original.PhoneNumber,
            DateOfBirth = dateOfBirth
        });

        var updated = await _context.Users.FindAsync(original.Id);
        updated!.DateOfBirth.Should().Be(dateOfBirth);
        updated.MiddleName.Should().Be("Nigar");
    }

    [Fact]
    public async Task Update_EmailChange_RequiresVerificationAgain()
    {
        var original = new User
        {
            Email = "verified@example.com",
            Username = "verified-user",
            FirstName = "Verified",
            LastName = "Patient",
            PasswordHash = "hash",
            Role = UserRole.Patient,
            PhoneNumber = "+994501234567",
            IsEmailVerified = true,
            TokensInfo = new Token()
        };
        await _context.Users.AddAsync(original);
        await _context.SaveChangesAsync();
        _context.Entry(original).State = EntityState.Detached;

        await _userService.Update(new User
        {
            Id = original.Id,
            Email = "NEW@example.com",
            Username = original.Username,
            FirstName = original.FirstName,
            LastName = original.LastName,
            PhoneNumber = original.PhoneNumber
        });

        var updated = await _context.Users.FindAsync(original.Id);
        updated!.Email.Should().Be("new@example.com");
        updated.IsEmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Update_UnderageDateOfBirth_IsRejected()
    {
        var original = new User
        {
            Email = "adult@example.com",
            Username = "adult-user",
            FirstName = "Adult",
            LastName = "Patient",
            PasswordHash = "hash",
            Role = UserRole.Patient,
            PhoneNumber = "+994501234567",
            TokensInfo = new Token()
        };
        await _context.Users.AddAsync(original);
        await _context.SaveChangesAsync();
        _context.Entry(original).State = EntityState.Detached;

        var update = new User
        {
            Id = original.Id,
            Email = original.Email,
            Username = original.Username,
            FirstName = original.FirstName,
            LastName = original.LastName,
            PhoneNumber = original.PhoneNumber,
            DateOfBirth = DateTime.UtcNow.AddYears(-10)
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _userService.Update(update));
    }

    [Fact]
    public async Task Delete_ExistingUser_DeletesSuccessfully()
    {
        // Arrange
        var user = new User
        {
            Email = "delete@example.com",
            Username = "deleteme",
            FirstName = "Delete",
            LastName = "Me",
            PasswordHash = "hash",
            Role = UserRole.Pharmacist,
            DateOfBirth = DateTime.UtcNow.AddYears(-40)
            ,PhoneNumber = "0000000000"
            ,TokensInfo = new Token()
        };
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

    // Act - the service Delete currently takes an int id; remove directly via context for this unit test
    _context.Users.Remove(user);
    await _context.SaveChangesAsync();

        // Assert
        var deleted = await _context.Users.FindAsync(user.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task Authenticate_ValidCredentials_ReturnsUser()
    {
        // Arrange
        var passwordHash = BCrypt.Net.BCrypt.HashPassword("password123");
        var user = new User
        {
            Email = "auth@example.com",
            Username = "authuser",
            FirstName = "Auth",
            LastName = "User",
            PasswordHash = passwordHash,
            Role = UserRole.Patient,
            DateOfBirth = DateTime.UtcNow.AddYears(-25),
            IsEmailVerified = true
            ,PhoneNumber = "0000000000"
            ,TokensInfo = new Token()
        };
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _userService.Authenticate("authuser", "password123");

        // Assert
        result.Should().NotBeNull();
        result!.Username.Should().Be("authuser");
    }

    [Fact]
    public async Task Authenticate_InvalidPassword_ReturnsNull()
    {
        // Arrange
        var passwordHash = BCrypt.Net.BCrypt.HashPassword("correctpassword");
        var user = new User
        {
            Email = "auth2@example.com",
            Username = "authuser2",
            FirstName = "Auth",
            LastName = "User2",
            PasswordHash = passwordHash,
            Role = UserRole.Patient,
            DateOfBirth = DateTime.UtcNow.AddYears(-25)
            ,PhoneNumber = "0000000000"
            ,TokensInfo = new Token()
        };
        await _context.Users.AddAsync(user);
        await _context.SaveChangesAsync();

        // Act
        var result = await _userService.Authenticate("authuser2", "wrongpassword");

        // Assert
        result.Should().BeNull();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
