using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using PIYA_API.Model;

namespace PIYA_API.Tests.Security;

/// <summary>
/// Cross-role authorization security tests.
/// Verifies that role-based access control prevents unauthorized access
/// between Patient, Doctor, Pharmacist, and Admin roles.
/// </summary>
public class AuthorizationSecurityTests : IClassFixture<PiyaWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly PiyaWebApplicationFactory _factory;

    public AuthorizationSecurityTests(PiyaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<string> RegisterAndLoginAsync(string role = "Patient") =>
        TestIdentity.CreateAndLoginAsync(_factory, _client, Enum.Parse<UserRole>(role));

    // ── Doctor-only endpoints ─────────────────────────────────

    [Fact]
    public async Task DoctorDashboard_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/doctor/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatePrescription_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.PostAsJsonAsync("/api/prescription", new
        {
            patientId = Guid.NewGuid(),
            diagnosis = "Hypertension",
            expiresAt = DateTime.UtcNow.AddDays(30)
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Admin-only endpoints ──────────────────────────────────

    [Fact]
    public async Task AdminUsers_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/v1/userslist");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminUsers_AsDoctor_Returns403()
    {
        var token = await RegisterAndLoginAsync("Doctor");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/v1/userslist");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AuditLogs_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/audit/all");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Pharmacy-only endpoints ───────────────────────────────

    [Fact]
    public async Task PharmacyManager_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync($"/api/pharmacymanager/{Guid.NewGuid()}/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Unauthenticated access ────────────────────────────────

    [Fact]
    public async Task ProtectedEndpoint_WithNoToken_Returns401()
    {
        // Clear any auth header
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/user/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithInvalidToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "this.is.not.a.valid.jwt");

        var response = await _client.GetAsync("/api/user/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredToken_Returns401()
    {
        // An otherwise well-formed but clearly expired JWT
        const string expiredToken =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IlRlc3QiLCJleHAiOjEwMDAwMDB9." +
            "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await _client.GetAsync("/api/user/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
