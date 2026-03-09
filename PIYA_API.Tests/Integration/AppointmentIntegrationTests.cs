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
        // Step 1: Register the patient.
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

    // Step 2: Register the future doctor as a patient first.
    // Self-registration is intentionally restricted to the Patient role.
        var doctorEmail = $"doctor-{Guid.NewGuid()}@example.com";
        var doctorRegister = new
        {
            email = doctorEmail,
            password = "Doctor@123",
            firstName = "Jane",
            lastName = "Smith",
            phoneNumber = "+994502222222",
            dateOfBirth = "1980-01-01",
            role = "Patient"
        };
        var doctorResponse = await _client.PostAsJsonAsync("/api/auth/register", doctorRegister);
        doctorResponse.StatusCode.Should().Be(HttpStatusCode.OK, "doctor bootstrap registration must succeed");
        var doctorData = await ParseRegisterResponse(doctorResponse);

        // Step 3: Elevate the second user to Doctor via the admin-only role assignment endpoint.
        var adminToken = await LoginSeededAdmin();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var assignRoleResponse = await _client.PostAsJsonAsync(
            $"/api/user/{doctorData["userId"]}/assign-role",
            new { role = "Doctor" });
        assignRoleResponse.StatusCode.Should().Be(HttpStatusCode.OK, "admin should be able to assign the Doctor role");

        // Step 4: Obtain a real hospital ID — query existing hospitals; if none, use the seeded admin
        //         and create one so the FK constraint is satisfied.
        var hospitalId = await GetOrCreateHospitalId();

        // Step 5: Create doctor profile (optional — service auto-creates missing doctors)
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

        // Step 6: Book appointment as patient using the elevated doctor's userId.
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

        // AppointmentController: [Route("api/[controller]")] + [HttpPost("book")]  →  POST /api/appointment/book
        var bookResponse = await _client.PostAsJsonAsync("/api/appointment/book", appointment);

        // Assert
        bookResponse.StatusCode.Should().Be(
            HttpStatusCode.Created,
            $"booking should succeed — body: {await bookResponse.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task DoctorAppointmentsEndpoint_ReturnsCurrentDoctorAppointments()
    {
        // Arrange: register doctor and patient, elevate the doctor via admin, then book an appointment.
        var patientEmail = $"patient-list-{Guid.NewGuid()}@example.com";
        var doctorEmail = $"doctor-list-{Guid.NewGuid()}@example.com";

        var patientRegister = new
        {
            email = patientEmail,
            password = "Patient@123",
            firstName = "Pat",
            lastName = "Ient",
            phoneNumber = "+994503333333",
            dateOfBirth = "1990-01-01",
            role = "Patient"
        };
        var doctorRegister = new
        {
            email = doctorEmail,
            password = "Doctor@123",
            firstName = "Doc",
            lastName = "Tor",
            phoneNumber = "+994504444444",
            dateOfBirth = "1985-01-01",
            role = "Patient"
        };

        var patientData = await ParseRegisterResponse(await _client.PostAsJsonAsync("/api/auth/register", patientRegister));
        var doctorData = await ParseRegisterResponse(await _client.PostAsJsonAsync("/api/auth/register", doctorRegister));
        var adminToken = await LoginSeededAdmin();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var assignRoleResponse = await _client.PostAsJsonAsync(
            $"/api/user/{doctorData["userId"]}/assign-role",
            new { role = "Doctor" });
        assignRoleResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-login after role assignment so the JWT carries the updated Doctor role claim.
        var reloginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = doctorEmail,
            password = "Doctor@123"
        });
        reloginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        doctorData["accessToken"] = await ParseAccessToken(reloginResponse);

        var hospitalId = await GetOrCreateHospitalId();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", patientData["accessToken"]);

        var appointment = new
        {
            doctorId = Guid.Parse(doctorData["userId"]),
            hospitalId,
            scheduledAt = DateTime.UtcNow.AddDays(1),
            reason = "Doctor endpoint contract test"
        };

        var bookResponse = await _client.PostAsJsonAsync("/api/appointment/book", appointment);
        bookResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: request doctor-scoped appointments with the doctor's token.
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", doctorData["accessToken"]);

        var doctorAppointmentsResponse = await _client.GetAsync("/api/doctor/appointments");

        // Assert
        doctorAppointmentsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await doctorAppointmentsResponse.Content.ReadAsStringAsync();
        body.Should().Contain("Doctor endpoint contract test");
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
            ["refreshToken"] = data.TryGetValue("refreshToken", out var rt) && rt.ValueKind == JsonValueKind.String
                                   ? rt.GetString()! : string.Empty,
            // The register endpoint returns userId directly — no JWT decoding needed
            ["userId"]       = data["userId"].GetString()!,
        };
    }

    /// <summary>
    /// Parses a login or register response and returns just the Bearer access token.
    /// Accepts either "accessToken" or the legacy "token" field name.
    /// </summary>
    private static async Task<string> ParseAccessToken(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        if (data.TryGetValue("accessToken", out var at) && at.ValueKind == JsonValueKind.String)
            return at.GetString()!;
        if (data.TryGetValue("token", out var t) && t.ValueKind == JsonValueKind.String)
            return t.GetString()!;

        throw new InvalidOperationException($"No access token field found in response: {json}");
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

        // No hospitals yet — log in as the seeded admin_piya demo user (Role=Admin)
        // and create one.  We cannot self-register an Admin because /api/auth/register
        // always assigns the Patient role regardless of the 'role' field in the body.
        var adminToken = await LoginSeededAdmin();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", adminToken);

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

    private async Task<string> LoginSeededAdmin()
    {
        var adminLogin = new
        {
            username = "admin_piya",
            password = "Test@1234"
        };
        var adminLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", adminLogin);
        adminLoginResponse.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "seeded admin_piya login must succeed — ensure DataSeeder ran on startup");
        return await ParseAccessToken(adminLoginResponse);
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
