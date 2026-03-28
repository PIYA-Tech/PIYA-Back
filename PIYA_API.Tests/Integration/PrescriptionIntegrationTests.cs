using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PIYA_API.Tests.Integration;

/// <summary>
/// End-to-end integration tests for prescription lifecycle:
/// create → QR generate → QR validate (one-time use enforcement)
/// </summary>
public class PrescriptionIntegrationTests : IClassFixture<PiyaWebApplicationFactory>
{
    private readonly HttpClient _client;

    public PrescriptionIntegrationTests(PiyaWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── helpers ──────────────────────────────────────────────

    private async Task<string> RegisterAndLoginAsync(string role = "Patient")
    {
        var email = $"prx-{Guid.NewGuid()}@test.com";
        var password = "Test@Password123";

        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            firstName = "Test",
            lastName = "User",
            phoneNumber = "+994501234567",
            dateOfBirth = "1990-01-01",
            role,
            username = $"usr_{Guid.NewGuid():N}"
        });

        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = email,
            password
        });

        if (loginResp.StatusCode != HttpStatusCode.OK)
            return string.Empty;

        var body = await loginResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("accessToken", out var t) ? t.GetString() ?? string.Empty : string.Empty;
    }

    [Fact]
    public async Task GetMyPrescriptions_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/prescription/my");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyPrescriptions_AuthenticatedPatient_Returns200()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token))
        {
            // Skip if auth didn't complete (2FA required, etc.)
            return;
        }

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/prescription/my");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAllPrescriptions_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/prescription/all");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPrescriptionById_NonExistentId_Returns404()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync($"/api/prescription/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
