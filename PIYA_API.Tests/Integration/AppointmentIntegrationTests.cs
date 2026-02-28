using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Xunit;
using FluentAssertions;

namespace PIYA_API.Tests.Integration;

/// <summary>
/// Integration tests for Appointment booking flow
/// </summary>
public class AppointmentIntegrationTests : IClassFixture<PiyaWebApplicationFactory>
{
    private readonly PiyaWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AppointmentIntegrationTests(PiyaWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task BookAppointment_EndToEndFlow_Success()
    {
        // Step 1: Register as patient — capture the userId returned directly in the response
        var patientEmail = $"patient-{Guid.NewGuid()}@example.com";
        var patientRegister = new
        {
            email = patientEmail,
            password = "Patient@123",
            firstName = "John",
            lastName = "Doe",
            phoneNumber = "+994501111111",
            dateOfBirth = "1990-01-01",
            role = "Patient"
        };
        var patientResponse = await _client.PostAsJsonAsync("/api/auth/register", patientRegister);
        patientResponse.StatusCode.Should().Be(HttpStatusCode.OK, "patient registration must succeed");
        var patientData = await ParseRegisterResponse(patientResponse);

        // Step 2: Register as doctor — capture the real userId and accessToken
        var doctorEmail = $"doctor-{Guid.NewGuid()}@example.com";
        var doctorRegister = new
        {
            email = doctorEmail,
            password = "Doctor@123",
            firstName = "Jane",
            lastName = "Smith",
            phoneNumber = "+994502222222",
            dateOfBirth = "1980-01-01",
            role = "Doctor"
        };
        var doctorResponse = await _client.PostAsJsonAsync("/api/auth/register", doctorRegister);
        doctorResponse.StatusCode.Should().Be(HttpStatusCode.OK, "doctor registration must succeed");
        var doctorData = await ParseRegisterResponse(doctorResponse);

        // Step 3: Obtain a real hospital ID — query existing hospitals; if none, register an Admin
        //         and create one so the FK constraint is satisfied.
        var hospitalId = await GetOrCreateHospitalId();

        // Step 4: Create doctor profile (optional — service auto-creates missing doctors)
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", doctorData["accessToken"]);

        var doctorProfile = new
        {
            specialization = "Cardiology",
            licenseNumber = $"DOC-{Guid.NewGuid().ToString()[..8]}",
            hospitalIds = new[] { hospitalId },
            consultationFee = 100,
            workingHours = new[]
            {
                new { dayOfWeek = 1, startTime = "09:00", endTime = "17:00" }
            }
        };
        // Best-effort — ignore failures (profile endpoint may not exist yet)
        await _client.PostAsJsonAsync("/api/doctor/profile", doctorProfile);

        // Step 5: Book appointment as patient using real doctorId from register response
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", patientData["accessToken"]);

        // AppointmentRequest expects: doctorId, hospitalId, scheduledAt, reason
        var appointment = new
        {
            doctorId  = Guid.Parse(doctorData["userId"]),
            hospitalId,
            scheduledAt = DateTime.UtcNow.AddDays(1),   // field name matches AppointmentRequest record
            reason    = "Regular checkup"
        };

        // AppointmentController is registered at api/appointment (singular — [Route("api/[controller]")])
        var bookResponse = await _client.PostAsJsonAsync("/api/appointment", appointment);

        // Assert
        bookResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            $"booking should succeed — body: {await bookResponse.Content.ReadAsStringAsync()}");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses a successful /api/auth/register response and returns a dictionary
    /// containing at minimum: accessToken, refreshToken, userId.
    /// </summary>
    private static async Task<Dictionary<string, string>> ParseRegisterResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return new Dictionary<string, string>
        {
            ["accessToken"]  = data["accessToken"].GetString()!,
            ["refreshToken"] = data["refreshToken"].GetString()!,
            // The register endpoint returns userId directly — no JWT decoding needed
            ["userId"]       = data["userId"].GetString()!,
        };
    }

    /// <summary>
    /// Returns the ID of a real hospital in the database.
    /// Strategy:
    ///   1. GET /api/hospital — if any hospitals exist, use the first one.
    ///   2. Otherwise register an Admin user and POST /api/hospital to create one.
    /// This avoids foreign-key violations that would cause a 500 in BookAppointmentAsync.
    /// </summary>
    private async Task<Guid> GetOrCreateHospitalId()
    {
        // Clear any auth header for the GET (it is [AllowAnonymous])
        _client.DefaultRequestHeaders.Authorization = null;

        var listResponse = await _client.GetAsync("/api/hospital");
        if (listResponse.IsSuccessStatusCode)
        {
            var json = await listResponse.Content.ReadAsStringAsync();
            var hospitals = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (hospitals?.Count > 0)
                return Guid.Parse(hospitals[0]["id"].GetString()!);
        }

        // No hospitals yet — register an Admin and create one
        var adminEmail = $"admin-{Guid.NewGuid()}@example.com";
        var adminRegister = new
        {
            email     = adminEmail,
            password  = "Admin@123",
            firstName = "Test",
            lastName  = "Admin",
            phoneNumber = "+994503333333",
            dateOfBirth = "1970-01-01",
            role = "Admin"
        };
        var adminRegisterResponse = await _client.PostAsJsonAsync("/api/auth/register", adminRegister);
        var adminData = await ParseRegisterResponse(adminRegisterResponse);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminData["accessToken"]);

        var hospitalPayload = new
        {
            name        = "Integration Test Hospital",
            address     = "123 Test Avenue",
            city        = "Baku",
            country     = "Azerbaijan",
            phoneNumber = "+994123456789",
            departments = new[] { "Cardiology", "General" }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/hospital", hospitalPayload);
        createResponse.IsSuccessStatusCode.Should().BeTrue(
            $"hospital creation must succeed — body: {await createResponse.Content.ReadAsStringAsync()}");

        var created = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            await createResponse.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        return Guid.Parse(created["id"].GetString()!);
    }

    /// <summary>
    /// Decodes a JWT access token and returns the value of the "sub" claim (user ID).
    /// Use this only when the register/login response does not include userId directly.
    /// </summary>
    private static string GetUserIdFromToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        return jwt.Claims.First(c => c.Type == "sub").Value;
    }
}
