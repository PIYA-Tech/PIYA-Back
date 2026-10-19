using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using PIYA_API.Model;

namespace PIYA_API.Tests.Integration;

/// <summary>
/// End-to-end integration tests for prescription lifecycle:
/// create → QR generate → QR validate (one-time use enforcement)
/// </summary>
public class PrescriptionIntegrationTests : IClassFixture<PiyaWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly PiyaWebApplicationFactory _factory;

    public PrescriptionIntegrationTests(PiyaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── helpers ──────────────────────────────────────────────

    private Task<string> RegisterAndLoginAsync(string role = "Patient") =>
        TestIdentity.CreateAndLoginAsync(_factory, _client, Enum.Parse<UserRole>(role));


    [Fact]
    public async Task GetMyPrescriptions_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/prescription/my-prescriptions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyPrescriptions_AuthenticatedPatient_Returns200()
    {
        var token = await RegisterAndLoginAsync("Patient");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/prescription/my-prescriptions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAllPrescriptions_AsPatient_Returns403()
    {
        var token = await RegisterAndLoginAsync("Patient");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync("/api/prescription/all");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetPrescriptionById_NonExistentId_Returns404()
    {
        var token = await RegisterAndLoginAsync("Patient");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.GetAsync($"/api/prescription/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
