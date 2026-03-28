using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;

namespace PIYA_API.Tests.Unit;

public class QRServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly QRService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _entityId = Guid.NewGuid();

    public QRServiceTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new PharmacyApiDbContext(options);

        // Seed the user required for FK navigation
        _context.Users.Add(new User
        {
            Id = _userId,
            Email = "qr_user@test.com",
            Username = "qr_user",
            FirstName = "QR",
            LastName = "User",
            PasswordHash = "hash",
            Role = UserRole.Patient,
            DateOfBirth = DateTime.UtcNow.AddYears(-25),
            PhoneNumber = "0000000000",
            TokensInfo = new Token()
        });
        _context.SaveChanges();

        var securityOptions = Options.Create(new SecurityOptions
        {
            QrSigningKey = "TEST_QR_SIGNING_KEY_32_CHARACTERS_LONG_MINIMUM",
            QrTokenExpiryMinutes = 5
        });

        _service = new QRService(
            _context,
            securityOptions,
            Mock.Of<ILogger<QRService>>(),
            Mock.Of<IAuditService>());
    }

    [Fact]
    public async Task GenerateQrToken_ReturnsNonEmptyToken()
    {
        var (token, tokenId) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);

        token.Should().NotBeNullOrWhiteSpace();
        tokenId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task GenerateQrToken_PersistsTokenRecord()
    {
        var (token, tokenId) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);

        var stored = await _context.QRTokens.FindAsync(tokenId);
        stored.Should().NotBeNull();
        stored!.IsUsed.Should().BeFalse();
        stored.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateQrToken_WithValidToken_ReturnsIsValidTrue()
    {
        var (token, _) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);

        var (isValid, entityId, entityType, expiresAt, error) = await _service.ValidateQrTokenAsync(token);

        isValid.Should().BeTrue();
        entityId.Should().Be(_entityId);
        entityType.Should().Be("Prescription");
        error.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateQrToken_WithFakeToken_ReturnsIsValidFalse()
    {
        var (isValid, _, _, _, error) = await _service.ValidateQrTokenAsync("fake.invalid.token");

        isValid.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task MarkTokenAsUsed_SetsIsUsedTrue()
    {
        var (token, tokenId) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);

        var marked = await _service.MarkTokenAsUsedAsync(token, _userId);

        marked.Should().BeTrue();
        var stored = await _context.QRTokens.FindAsync(tokenId);
        stored!.IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkTokenAsUsed_SecondTime_ReturnsFalse()
    {
        var (token, _) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);
        await _service.MarkTokenAsUsedAsync(token, _userId);

        var secondUse = await _service.MarkTokenAsUsedAsync(token, _userId);

        secondUse.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeToken_SetsIsRevokedTrue()
    {
        var (token, tokenId) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);

        var revoked = await _service.RevokeTokenAsync(token, _userId, "Test revocation");

        revoked.Should().BeTrue();
        var stored = await _context.QRTokens.FindAsync(tokenId);
        stored!.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateQrToken_AfterRevoke_ReturnsIsValidFalse()
    {
        var (token, _) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);
        await _service.RevokeTokenAsync(token, _userId, "Revoked for test");

        var (isValid, _, _, _, _) = await _service.ValidateQrTokenAsync(token);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task GetTokenStatus_FreshToken_ReturnsActive()
    {
        var (token, _) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);

        var (status, expiresAt) = await _service.GetTokenStatusAsync(token);

        status.Should().Be(QRTokenStatus.Active);
        expiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task GetTokenStatus_AfterUse_ReturnsUsed()
    {
        var (token, _) = await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);
        await _service.MarkTokenAsUsedAsync(token, _userId);

        var (status, _) = await _service.GetTokenStatusAsync(token);

        status.Should().Be(QRTokenStatus.Used);
    }

    [Fact]
    public async Task GetTokenHistory_ReturnsAllTokensForEntity()
    {
        await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);
        await _service.GenerateQrTokenAsync(_entityId, "Prescription", _userId);

        var history = await _service.GetTokenHistoryAsync(_entityId, "Prescription");

        history.Should().HaveCount(2);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
