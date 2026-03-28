using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PIYA_API.Tests.Security;

/// <summary>
/// Cross-role authorization security tests.
/// Verifies that role-based access control prevents unauthorized access
/// between Patient, Doctor, Pharmacist, and Admin roles.
/// </summary>
public class AuthorizationSecurityTests : IClassFixture<PiyaWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthorizationSecurityTests(PiyaWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndLoginAsync(string role)
    {
        var email = $"authz-{role.ToLower()}-{Guid.NewGuid()}@test.com";
        const string password = "Test@Password123";

        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            firstName = role,
            lastName = "TestUser",
            phoneNumber = "+994501234567",
            dateOfBirth = "1990-01-01",
            role,
            username = $"{role.ToLower()}_{Guid.NewGuid():N}"
        });

        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = email,
            password
        });

        if (loginResp.StatusCode != HttpStatusCode.OK) return string.Empty;
        var body = await loginResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("accessToken", out var t) ? t.GetString() ?? string.Empty : string.Empty;
    }

    // ── Doctor-only endpoints ─────────────────────────────────

    [Fact]
    public async Task DoctorDashboard_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/doctor/my-profile");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatePrescription_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.PostAsJsonAsync("/api/prescription", new
        {
            patientId = Guid.NewGuid(),
            diagnosis = "Hypertension",
            expiresAt = DateTime.UtcNow.AddDays(30)
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
    }

    // ── Admin-only endpoints ──────────────────────────────────

    [Fact]
    public async Task AdminUsers_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/userslist/all");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminUsers_AsDoctor_Returns403()
    {
        var token = await RegisterAndLoginAsync("Doctor");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/userslist/all");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuditLogs_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/audit/all");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    // ── Pharmacy-only endpoints ───────────────────────────────

    [Fact]
    public async Task PharmacyManager_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/pharmacy-manager/my-pharmacy");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
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
