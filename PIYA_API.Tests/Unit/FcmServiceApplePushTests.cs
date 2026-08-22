using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class FcmServiceApplePushTests
{
    [Fact]
    public async Task NativeIosToken_IsDeliveredThroughApnsWithRootRoutingData()
    {
        var keyPath = Path.GetTempFileName();
        try
        {
            using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                await File.WriteAllTextAsync(keyPath, key.ExportPkcs8PrivateKeyPem());

            await using var db = new PharmacyApiDbContext(
                new DbContextOptionsBuilder<PharmacyApiDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var user = new User
            {
                Id = Guid.NewGuid(), Username = "ios-patient", PasswordHash = "hash",
                FirstName = "Demo", LastName = "Patient", Email = "ios@piya.test",
                PhoneNumber = "+994501234567", Role = UserRole.Patient, IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            var deviceToken = new string('a', 64);
            db.Users.Add(user);
            db.DeviceTokens.Add(new DeviceToken
            {
                Id = Guid.NewGuid(), UserId = user.Id, User = user,
                Token = deviceToken, Platform = "ios", IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            HttpRequestMessage? capturedRequest = null;
            string? capturedBody = null;
            var handler = new RecordingHandler(async request =>
            {
                capturedRequest = request;
                capturedBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(item => item.CreateClient("ApplePushNotifications"))
                .Returns(new HttpClient(handler));
            var service = new FcmService(
                db, factory.Object,
                Options.Create(new FirebaseOptions { Enabled = false }),
                Options.Create(new ApplePushOptions
                {
                    Enabled = true, TeamId = "TEAM123456", KeyId = "KEY1234567",
                    BundleId = "com.piya.life", PrivateKeyPath = keyPath, UseSandbox = true
                }),
                Mock.Of<ILogger<FcmService>>());

            var sent = await service.SendNotificationAsync(
                deviceToken, "Emergency record accessed", "A doctor opened the record.",
                new Dictionary<string, string> { ["type"] = "emergencyAccess", ["grantId"] = "grant" });

            sent.Should().BeTrue();
            capturedRequest.Should().NotBeNull();
            capturedRequest!.RequestUri!.Host.Should().Be("api.sandbox.push.apple.com");
            capturedRequest.Version.Should().Be(HttpVersion.Version20);
            capturedRequest.Headers.GetValues("apns-topic").Should().ContainSingle("com.piya.life");
            capturedRequest.Headers.Authorization!.Scheme.Should().Be("bearer");
            using var json = JsonDocument.Parse(capturedBody!);
            json.RootElement.GetProperty("type").GetString().Should().Be("emergencyAccess");
            json.RootElement.GetProperty("grantId").GetString().Should().Be("grant");
            json.RootElement.GetProperty("aps").GetProperty("alert")
                .GetProperty("title").GetString().Should().Be("Emergency record accessed");
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
