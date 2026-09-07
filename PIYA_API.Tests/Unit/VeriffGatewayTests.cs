using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class VeriffGatewayTests
{
    private const string Key = "test-key-not-a-real-credential", Secret = "test-secret-not-a-real-credential";
    private static readonly Guid Session = Guid.NewGuid(), Patient = Guid.NewGuid();
    private static VeriffOptions Config(bool live = true) => new() { Enabled = true, LiveMode = live, ApiKey = Key, SharedSecret = Secret };
    private static VeriffVerificationProviderGateway Gateway(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler, VeriffOptions? config = null) =>
        new(new HttpClient(new Stub(handler)), Options.Create(config ?? Config()));
    private static HttpResponseMessage Signed(object body, string? signature = null, string key = Key) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Headers.Add("X-AUTH-CLIENT", key);
        response.Headers.Add("X-HMAC-SIGNATURE", signature ?? Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), bytes)).ToLowerInvariant());
        return response;
    }
    private static object Decision(int code, string status, Guid? session = null) => new {
        status = "success", verification = new {
            id = (session ?? Session).ToString(), endUserId = Patient.ToString(), code, status,
            person = new { firstName = "Ayla", lastName = "Aliyeva", dateOfBirth = "1992-01-12" },
            document = new { validUntil = "2035-01-01" }
        }
    };
    [Fact] public void UnconfiguredAndInsuranceCapabilitiesRemainUnavailable() {
        var gateway = Gateway(_ => throw new Exception("No network expected"), new VeriffOptions());
        gateway.GetCapability(PatientVerificationKind.Identity).IsConnected.Should().BeFalse();
        Gateway(_ => throw new Exception()).GetCapability(PatientVerificationKind.Insurance).IsConnected.Should().BeFalse();
    }
    [Theory]
    [InlineData("http://stationapi.veriff.com/")]
    [InlineData("https://stationapi.veriff.com.attacker.invalid/")]
    [InlineData("https://user:pass@stationapi.veriff.com/")]
    [InlineData("https://stationapi.veriff.com/?token=secret")]
    public void UntrustedConfigurationCannotSendCredentials(string address) {
        var config = Config(); config.BaseUrl = address;
        Gateway(_ => throw new Exception(), config).GetCapability(PatientVerificationKind.Identity).IsConnected.Should().BeFalse();
    }
    [Fact] public async Task CreateUsesSignedServerRequestAndOnlyReturnsHostedAction() {
        var gateway = Gateway(async request => {
            request.RequestUri!.AbsolutePath.Should().Be("/v1/sessions");
            var bytes = await request.Content!.ReadAsByteArrayAsync();
            request.Headers.GetValues("X-HMAC-SIGNATURE").Single().Should().Be(Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), bytes)).ToLowerInvariant());
            using var body = JsonDocument.Parse(bytes);
            body.RootElement.GetProperty("verification").GetProperty("endUserId").GetString().Should().Be(Patient.ToString());
            body.RootElement.GetProperty("verification").GetProperty("document").GetProperty("type").GetString().Should().Be("ID_CARD");
            Encoding.UTF8.GetString(bytes).Should().NotContain("policyReference").And.NotContain("dateOfBirth");
            return Signed(new { status = "success", verification = new { id = Session, url = "https://magic.veriff.me/v/test-session" } });
        });
        var result = await gateway.StartAsync(new(Patient, PatientVerificationKind.Identity, "AZ", "NationalID", null, null));
        result.Status.Should().Be(PatientVerificationStatus.RequiresAction);
        result.ActionUrl.Should().StartWith("https://magic.veriff.me/");
    }
    [Theory]
    [InlineData(9001, "approved", PatientVerificationStatus.Verified)]
    [InlineData(9001, "not-approved", PatientVerificationStatus.Pending)]
    [InlineData(9102, "declined", PatientVerificationStatus.Rejected)]
    [InlineData(9103, "resubmission_requested", PatientVerificationStatus.RequiresAction)]
    [InlineData(9104, "expired", PatientVerificationStatus.Expired)]
    [InlineData(9121, "abandoned", PatientVerificationStatus.Expired)]
    [InlineData(9999, "unknown", PatientVerificationStatus.Pending)]
    public async Task MapsOnlyDocumentedDecisions(int code, string status, PatientVerificationStatus expected) {
        var gateway = Gateway(request => {
            request.Headers.GetValues("X-HMAC-SIGNATURE").Single().Should().Be(Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(Session.ToString()))).ToLowerInvariant());
            return Task.FromResult(Signed(Decision(code, status)));
        });
        var result = await gateway.RefreshAsync(PatientVerificationKind.Identity, Session.ToString());
        result.Status.Should().Be(expected);
        if (expected == PatientVerificationStatus.Verified) result.Identity!.SubjectId.Should().Be(Patient);
    }
    [Fact] public async Task TestModeCannotProduceVerifiedIdentity() {
        var result = await Gateway(_ => Task.FromResult(Signed(Decision(9001, "approved"))), Config(live: false))
            .RefreshAsync(PatientVerificationKind.Identity, Session.ToString());
        result.Status.Should().Be(PatientVerificationStatus.Pending);
        result.StatusReasonCode.Should().Be("provider_test_mode");
        result.Identity.Should().BeNull();
    }
    [Fact] public async Task NoDecisionYetIsPendingNotVerified() {
        var result = await Gateway(_ => Task.FromResult(Signed(new { status = "success", verification = (object?)null })))
            .RefreshAsync(PatientVerificationKind.Identity, Session.ToString());
        result.Status.Should().Be(PatientVerificationStatus.Pending);
    }
    [Theory]
    [InlineData("wrong-signature", Key)]
    [InlineData("", Key)]
    [InlineData(null, "wrong-client")]
    public async Task RejectsUnauthenticatedResponses(string? signature, string key) {
        var action = () => Gateway(_ => Task.FromResult(Signed(Decision(9001, "approved"), signature, key)))
            .RefreshAsync(PatientVerificationKind.Identity, Session.ToString());
        await action.Should().ThrowAsync<InvalidOperationException>();
    }
    [Fact] public async Task RejectsCrossSessionDecision() {
        var action = () => Gateway(_ => Task.FromResult(Signed(Decision(9001, "approved", Guid.NewGuid()))))
            .RefreshAsync(PatientVerificationKind.Identity, Session.ToString());
        await action.Should().ThrowAsync<InvalidOperationException>();
    }
    [Fact] public async Task RejectsUntrustedHostedUrl() {
        var action = () => Gateway(_ => Task.FromResult(Signed(new { status = "success", verification = new { id = Session, url = "https://veriff.me.attacker.invalid/capture" } })))
            .StartAsync(new(Patient, PatientVerificationKind.Identity, "AZ", "NationalID", null, null));
        await action.Should().ThrowAsync<InvalidOperationException>();
    }
    [Fact] public async Task IncompleteApprovedPersonCannotVerify() {
        var result = await Gateway(_ => Task.FromResult(Signed(new { status = "success", verification = new { id = Session, code = 9001, status = "approved" } })))
            .RefreshAsync(PatientVerificationKind.Identity, Session.ToString());
        result.Status.Should().Be(PatientVerificationStatus.Rejected);
    }
    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
