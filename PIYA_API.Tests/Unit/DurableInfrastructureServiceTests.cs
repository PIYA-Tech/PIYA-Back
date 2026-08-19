using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class DurableInfrastructureServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context = new(
        new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Webhook_PrivateDestination_IsRejectedBeforePersistence()
    {
        var service = MakeWebhookService();

        var action = () => service.RegisterWebhookAsync(
            "https://127.0.0.1/hooks/piya", [WebhookEventType.PrescriptionIssued]);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*private or reserved*");
        (await _context.WebhookSubscriptions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Webhook_Send_QueuesDurableDeliveryWithoutInlineHttpCall()
    {
        var webhook = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            Url = "https://hooks.example.test/piya",
            Events = [WebhookEventType.PrescriptionIssued],
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.WebhookSubscriptions.Add(webhook);
        await _context.SaveChangesAsync();
        var handler = new CountingHandler();
        var service = MakeWebhookService(handler);

        var queued = await service.SendWebhookAsync(
            webhook.Id, WebhookEventType.PrescriptionIssued, new { id = Guid.NewGuid() });

        queued.Should().BeTrue();
        handler.RequestCount.Should().Be(0);
        var delivery = await _context.WebhookDeliveries.SingleAsync();
        delivery.WebhookId.Should().Be(webhook.Id);
        delivery.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Consent_NewDecision_RevokesPriorDecisionAndPreservesHistory()
    {
        var user = MakeUser();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        var service = new GdprComplianceService(
            _context, Mock.Of<IAuditService>(), Mock.Of<ILogger<GdprComplianceService>>());

        var granted = await service.RecordConsentAsync(user.Id, "care analytics", true);
        var denied = await service.RecordConsentAsync(user.Id, "care analytics", false);

        (await service.HasConsentAsync(user.Id, "care analytics")).Should().BeFalse();
        granted.RevokedAt.Should().NotBeNull();
        denied.RevokedAt.Should().BeNull();
        (await service.GetUserConsentsAsync(user.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Registry_LastSync_UsesDurableSourceWatermark()
    {
        var sourceTimestamp = DateTime.UtcNow.AddHours(-2);
        _context.IntegrationSyncStates.Add(new IntegrationSyncState
        {
            Key = "azerbaijan-pharmaceutical-registry",
            SourceLastModifiedAt = sourceTimestamp,
            LastSuccessfulSyncAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        var service = new AzerbaijanPharmaceuticalRegistryService(
            _context,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<AzerbaijanPharmaceuticalRegistryService>>());

        var result = await service.GetLastSyncDateAsync();

        result.Should().Be(sourceTimestamp);
    }

    private WebhookService MakeWebhookService(HttpMessageHandler? handler = null)
    {
        var protector = new Mock<IDataProtector>();
        var provider = new Mock<IDataProtectionProvider>();
        provider.Setup(item => item.CreateProtector(It.IsAny<string>())).Returns(protector.Object);
        return new WebhookService(
            new HttpClient(handler ?? new CountingHandler()),
            _context,
            provider.Object,
            Mock.Of<ILogger<WebhookService>>());
    }

    private static User MakeUser() => new()
    {
        Id = Guid.NewGuid(),
        Username = $"user-{Guid.NewGuid():N}",
        FirstName = "Test",
        LastName = "User",
        Email = $"{Guid.NewGuid():N}@test.piya",
        PhoneNumber = "+994500000000",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    public void Dispose() => _context.Dispose();
}
