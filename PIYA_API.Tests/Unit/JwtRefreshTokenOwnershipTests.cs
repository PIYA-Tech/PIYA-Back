using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class JwtRefreshTokenOwnershipTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly JwtService _service;

    public JwtRefreshTokenOwnershipTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new PharmacyApiDbContext(options);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SecretKey"] = "UNIT_TEST_JWT_SECRET_KEY_AT_LEAST_32_CHARACTERS",
                ["Jwt:Issuer"] = "PIYA_Test",
                ["Jwt:Audience"] = "PIYA_Test_Clients"
            })
            .Build();
        _service = new JwtService(
            _context,
            configuration,
            Mock.Of<IDistributedCacheWrapper>(),
            Options.Create(new SecurityOptions()),
            Mock.Of<ILogger<JwtService>>());
    }

    [Fact]
    public async Task RevokeRefreshToken_DifferentPrincipal_DoesNotDeleteSession()
    {
        const string rawToken = "owner-refresh-token";
        var ownerId = Guid.NewGuid();
        var token = MakeToken(rawToken, ownerId);
        _context.Tokens.Add(token);
        await _context.SaveChangesAsync();

        var revoked = await _service.RevokeRefreshTokenAsync(rawToken, Guid.NewGuid());

        revoked.Should().BeFalse();
        (await _context.Tokens.FindAsync(token.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task RevokeRefreshToken_OwningPrincipal_DeletesSession()
    {
        const string rawToken = "owner-refresh-token";
        var ownerId = Guid.NewGuid();
        var token = MakeToken(rawToken, ownerId);
        _context.Tokens.Add(token);
        await _context.SaveChangesAsync();

        var revoked = await _service.RevokeRefreshTokenAsync(rawToken, ownerId);

        revoked.Should().BeTrue();
        (await _context.Tokens.FindAsync(token.Id)).Should().BeNull();
    }

    private static Token MakeToken(string rawToken, Guid ownerId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = ownerId,
        RefreshToken = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)))
            .ToLowerInvariant(),
        AccessToken = string.Empty,
        Family = Guid.NewGuid(),
        CreationTime = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddMinutes(15)
    };

    public void Dispose() => _context.Dispose();
}
