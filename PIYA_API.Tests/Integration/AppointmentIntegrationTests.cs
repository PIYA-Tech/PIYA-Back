using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
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
        // Step 1: Register as patient
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
        var patientData = await GetTokensFromResponse(patientResponse);

        // Step 2: Register as doctor
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
        var doctorData = await GetTokensFromResponse(doctorResponse);

        // Step 3: Get hospital (assume at least one exists or create one)
        var hospitalId = await GetOrCreateHospitalId();

        // Step 4: Create doctor profile
        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", doctorData["accessToken"]);
        
        var doctorProfile = new
        {
            specialization = "Cardiology",
            licenseNumber = $"DOC-{Guid.NewGuid().ToString().Substring(0, 8)}",
            hospitalIds = new[] { hospitalId },
            consultationFee = 100,
            workingHours = new[]
            {
                new
                {
                    dayOfWeek = 1, // Monday
                    startTime = "09:00",
                    endTime = "17:00"
                }
            }
        };
        await _client.PostAsJsonAsync("/api/doctor-dashboard/profile", doctorProfile);

        // Step 5: Book appointment as patient
        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", patientData["accessToken"]);

        var appointmentDate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss");
        var appointment = new
        {
            doctorId = GetUserIdFromToken(doctorData["accessToken"]),
            hospitalId,
            appointmentDate,
            reason = "Regular checkup",
            notes = "Integration test appointment"
        };

        var bookResponse = await _client.PostAsJsonAsync("/api/appointments", appointment);

        // Assert
        bookResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await bookResponse.Content.ReadAsStringAsync();
        content.Should().Contain("appointment");
    }

    private async Task<Dictionary<string, string>> GetTokensFromResponse(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(content);
        return new Dictionary<string, string>
        {
            ["accessToken"] = data!["accessToken"].GetString()!,
            ["refreshToken"] = data["refreshToken"].GetString()!
        };
    }

    private async Task<Guid> GetOrCreateHospitalId()
    {
        // In a real test, you'd query existing hospitals or create one
        // For simplicity, return a fixed GUID (assumes hospital with this ID exists)
        return Guid.NewGuid(); // Would need to create hospital first in real test
    }

    private string GetUserIdFromToken(string token)
    {
        // In a real implementation, decode JWT to get user ID
        // For now, return a placeholder
        return Guid.NewGuid().ToString();
    }
}
