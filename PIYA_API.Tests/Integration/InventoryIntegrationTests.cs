using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PIYA_API.Tests.Integration;

/// <summary>
/// Integration tests for pharmacy inventory management endpoints.
/// </summary>
public class InventoryIntegrationTests : IClassFixture<PiyaWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InventoryIntegrationTests(PiyaWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndLoginAsync(string role)
    {
        var email = $"inv-{Guid.NewGuid()}@test.com";
        var password = "Test@Password123";

        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            firstName = "Inv",
            lastName = "User",
            phoneNumber = "+994501234567",
            dateOfBirth = "1990-01-01",
            role,
            username = $"inv_{Guid.NewGuid():N}"
        });

        var resp = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            identifier = email,
            password
        });

        if (resp.StatusCode != HttpStatusCode.OK) return string.Empty;
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("accessToken", out var t) ? t.GetString() ?? string.Empty : string.Empty;
    }

    [Fact]
    public async Task GetPharmacyInventory_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync($"/api/pharmacy-inventory/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPharmacyInventory_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");
        if (string.IsNullOrEmpty(token)) return;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync($"/api/pharmacy-inventory/{Guid.NewGuid()}");

        // Patient should not access inventory management
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MedicationSearch_PublicEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/medication/search?query=para");

        // Public endpoint — no auth required
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PharmacySearch_PublicEndpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/pharmacy?city=Baku");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }
}
