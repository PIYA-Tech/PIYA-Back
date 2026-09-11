using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Cryptography;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class TwoFactorAuthServiceSecurityTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;

    public TwoFactorAuthServiceSecurityTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new PharmacyApiDbContext(options);
    }

    [Fact]
    public async Task BeginSetup_DoesNotEnableTwoFactorBeforePossessionIsVerified()
    {
        var userId = Guid.NewGuid();
        _context.Users.Add(new User
        {
            Id = userId,
            Username = "two-factor-user",
            FirstName = "Two",
            LastName = "Factor",
            Email = "two-factor@example.test",
            PhoneNumber = "+15555550100"
        });
        await _context.SaveChangesAsync();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(x => x.HashPassword(It.IsAny<string>())).Returns("hashed-backup");
        var service = MakeService(hasher.Object, Mock.Of<IDistributedCacheWrapper>());

        var setup = await service.EnableTwoFactorAsync(userId, TwoFactorMethod.TOTP);

        setup.SecretKey.Should().NotBeNullOrWhiteSpace();
        setup.BackupCodes.Should().HaveCount(10);
        setup.BackupCodes.Should().OnlyHaveUniqueItems();
        setup.BackupCodes.Should().OnlyContain(
            code => System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d{8}$"));
        var persisted = await _context.TwoFactorAuths.SingleAsync(x => x.UserId == userId);
        persisted.IsEnabled.Should().BeFalse();
        persisted.EnabledAt.Should().BeNull();
        persisted.SecretKey.Should().NotBe(setup.SecretKey, "TOTP secrets must be encrypted at rest");
    }

    [Fact]
    public async Task LoginChallenge_IsHashedAndCanBeConsumedOnlyOnce()
    {
        string? storedHash = null;
        var cache = new Mock<IDistributedCacheWrapper>();
        cache.Setup(x => x.SetStringAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, TimeSpan, CancellationToken>(
                (_, value, _, _) => storedHash = value)
            .Returns(Task.CompletedTask);
        cache.Setup(x => x.GetStringAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => storedHash);
        cache.Setup(x => x.RemoveAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, _) => storedHash = null)
            .Returns(Task.CompletedTask);
        var service = MakeService(Mock.Of<IPasswordHasher>(), cache.Object);
        var userId = Guid.NewGuid();
        _context.Users.Add(new User { Id = userId, Username = "challenge-test", Email = "challenge@example.test", FirstName = "Test", LastName = "Account", PhoneNumber = "", IsActive = true });
        await _context.SaveChangesAsync();

        var rawChallenge = await service.IssueChallenge(userId);

        storedHash.Should().NotBeNullOrWhiteSpace().And.NotBe(rawChallenge);
        (await service.ValidateChallenge(userId, rawChallenge)).Should().BeTrue();
        (await service.ConsumeChallenge(userId, rawChallenge)).Should().BeTrue();
        (await service.ConsumeChallenge(userId, rawChallenge)).Should().BeFalse();

        var staleChallenge = await service.IssueChallenge(userId);
        var user = await _context.Users.SingleAsync(u => u.Id == userId);
        user.PasswordHash = "replaced-password-hash";
        await _context.SaveChangesAsync();
        (await service.ValidateChallenge(userId, staleChallenge)).Should().BeFalse();
    }

    [Fact]
    public async Task ExplicitlyDisabledConfiguration_CannotBeReenabledByVerifyEndpoint()
    {
        var userId = Guid.NewGuid();
        _context.TwoFactorAuths.Add(new TwoFactorAuth
        {
            UserId = userId,
            IsEnabled = false,
            Method = TwoFactorMethod.TOTP,
            SecretKey = "JBSWY3DPEHPK3PXP"
        });
        await _context.SaveChangesAsync();
        var cache = new Mock<IDistributedCacheWrapper>();
        cache.Setup(x => x.GetStringAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var service = MakeService(Mock.Of<IPasswordHasher>(), cache.Object);

        (await service.VerifyCodeAsync(userId, "000000")).Should().BeFalse();

        (await _context.TwoFactorAuths.FindAsync(
            _context.TwoFactorAuths.Single().Id))!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task LegacyPlaintextTotpSecret_IsMigratedAfterSuccessfulVerification()
    {
        const string legacySecret = "JBSWY3DPEHPK3PXP";
        var userId = Guid.NewGuid();
        _context.TwoFactorAuths.Add(new TwoFactorAuth
        {
            UserId = userId,
            IsEnabled = true,
            Method = TwoFactorMethod.TOTP,
            SecretKey = legacySecret
        });
        await _context.SaveChangesAsync();
        var service = MakeService(
            Mock.Of<IPasswordHasher>(), Mock.Of<IDistributedCacheWrapper>());

        (await service.VerifyCodeAsync(userId, GenerateCurrentTotp(legacySecret))).Should().BeTrue();

        (await _context.TwoFactorAuths.SingleAsync()).SecretKey
            .Should().NotBe(legacySecret);
    }

    [Fact]
    public async Task SmsOtp_IsHashedInCacheAndConsumedAfterVerification()
    {
        var userId = Guid.NewGuid();
        _context.Users.Add(new User
        {
            Id = userId,
            Username = "sms-user",
            FirstName = "Sms",
            LastName = "User",
            Email = "sms@example.test",
            PhoneNumber = "+15555550101",
            TwoFactorAuth = new TwoFactorAuth
            {
                UserId = userId,
                IsEnabled = true,
                Method = TwoFactorMethod.SMS
            }
        });
        await _context.SaveChangesAsync();

        var values = new Dictionary<string, string>();
        var cache = new Mock<IDistributedCacheWrapper>();
        cache.Setup(x => x.GetStringAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                values.TryGetValue(key, out var value) ? value : null);
        cache.Setup(x => x.SetStringAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, TimeSpan, CancellationToken>(
                (key, value, _, _) => values[key] = value)
            .Returns(Task.CompletedTask);
        cache.Setup(x => x.RemoveAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => values.Remove(key))
            .Returns(Task.CompletedTask);
        string? deliveredCode = null;
        var sms = new Mock<ISmsService>();
        sms.Setup(x => x.SendVerificationCodeAsync(
                It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, code) => deliveredCode = code)
            .ReturnsAsync(true);
        var service = MakeService(
            Mock.Of<IPasswordHasher>(), cache.Object, sms.Object);

        (await service.SendSmsCodeAsync(userId)).Should().BeTrue();

        deliveredCode.Should().MatchRegex(@"^\d{6}$");
        values[$"2fa:otp:{userId}"].Should().NotBe(deliveredCode);
        (await service.VerifyCodeAsync(userId, deliveredCode!)).Should().BeTrue();
        values.Should().NotContainKey($"2fa:otp:{userId}");
    }

    private TwoFactorAuthService MakeService(
        IPasswordHasher passwordHasher,
        IDistributedCacheWrapper cache,
        ISmsService? smsService = null) =>
        new(
            _context,
            passwordHasher,
            cache,
            Mock.Of<IFcmService>(),
            smsService ?? Mock.Of<ISmsService>(),
            Mock.Of<IEmailService>(),
            new EphemeralDataProtectionProvider(),
            Mock.Of<ILogger<TwoFactorAuthService>>());

    private static string GenerateCurrentTotp(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in secret)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits < 8)
                continue;
            bytes.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
        }

        var timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        using var hmac = new HMACSHA1(bytes.ToArray());
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);
        return (binary % 1_000_000).ToString("D6");
    }

    public void Dispose() => _context.Dispose();
}
