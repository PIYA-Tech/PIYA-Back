using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class SecurityHardeningServicePrivacyTests
{
    [Theory]
    [InlineData("O'Connor follow-up appointment")]
    [InlineData("Patient said: don't change the dosage")]
    [InlineData("select a pharmacy from the list")]
    public async Task SqlDetection_DoesNotFlagBenignClinicalText(string input)
    {
        var service = CreateService(Mock.Of<IDistributedCacheWrapper>());

        (await service.DetectSqlInjectionAsync(input)).Should().BeFalse();
    }

    [Theory]
    [InlineData("' OR 1=1--")]
    [InlineData("1 UNION SELECT password FROM users")]
    [InlineData("; DROP TABLE prescriptions")]
    public async Task SqlDetection_StillFlagsContextualInjection(string input)
    {
        var service = CreateService(Mock.Of<IDistributedCacheWrapper>());

        (await service.DetectSqlInjectionAsync(input)).Should().BeTrue();
    }

    [Fact]
    public async Task FailedLoginCache_HashesNormalizedIdentifier_AndOmitsItFromValue()
    {
        string? storedKey = null;
        string? storedValue = null;
        var cache = new Mock<IDistributedCacheWrapper>();
        cache.Setup(candidate => candidate.GetStringAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        cache.Setup(candidate => candidate.SetStringAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, TimeSpan, CancellationToken>(
                (key, value, _, _) =>
                {
                    storedKey = key;
                    storedValue = value;
                })
            .Returns(Task.CompletedTask);
        var service = CreateService(cache.Object);

        await service.RecordFailedLoginAttemptAsync(
            " Patient@Example.Test ",
            "192.0.2.10");

        storedKey.Should().MatchRegex(
            "^security:failed_logins:[A-F0-9]{64}$");
        storedKey!.ToLowerInvariant().Should().NotContain("patient");
        storedValue!.ToLowerInvariant().Should().NotContain(
            "patient@example.test");
        var attempts = JsonSerializer.Deserialize<List<FailedLoginAttempt>>(
            storedValue);
        attempts.Should().ContainSingle();
        attempts![0].Email.Should().BeEmpty();
    }

    private static SecurityHardeningService CreateService(
        IDistributedCacheWrapper cache) =>
        new(
            Mock.Of<ILogger<SecurityHardeningService>>(),
            Mock.Of<IServiceScopeFactory>(),
            cache);
}
