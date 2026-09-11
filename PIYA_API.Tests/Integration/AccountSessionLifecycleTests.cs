using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Integration;

// HTTP-level regressions for the confirmed audit defects. Factory refuses
// application databases; every record here is unique, synthetic test data.
public class AccountSessionLifecycleTests(PiyaWebApplicationFactory factory) : IClassFixture<PiyaWebApplicationFactory>
{
    private const string Password = "Synthetic-Test!583Original";

    private async Task<(User User, TokenResponse Token)> CreateAccount(UserRole role = UserRole.Patient)
    {
        await factory.EnsureMigratedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        var id = Guid.NewGuid();
        var user = new User {
            Id = id, Username = "session-test-" + id.ToString("N"), Email = id + "@example.invalid",
            FirstName = "Synthetic", LastName = "Test account", PhoneNumber = "", Role = role,
            IsActive = true, IsEmailVerified = true,
            PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().HashPassword(Password)
        };
        db.Users.Add(user);
        if (role == UserRole.Doctor) db.DoctorProfiles.Add(new DoctorProfile {
            Id = Guid.NewGuid(), UserId = id, LicenseNumber = "TEST-NOT-A-REAL-LICENSE",
            LicenseExpiryDate = DateTime.UtcNow.AddYears(1), Specialization = MedicalSpecialization.GeneralPractice
        });
        await db.SaveChangesAsync();
        var token = await scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateSecurityToken(user.Username);
        return (user, token!);
    }

    private HttpClient Client(TokenResponse token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        client.DefaultRequestHeaders.Add("X-PIYA-Client", "iOS");
        return client;
    }

    private async Task MutateUser(Guid id, Action<User> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        change(await db.Users.SingleAsync(u => u.Id == id));
        await db.SaveChangesAsync();
    }

    private static async Task AssertRevoked(HttpClient client, TokenResponse token)
    {
        (await client.GetAsync("/api/patient/medications")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/patient/medications", new { displayName = "[TEST] synthetic", dosage = "fixture", supplyUnit = "sample" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PasswordReset_InvalidatesAccessRefreshAndPendingChallenge_NewPasswordStillWorks()
    {
        var (user, token) = await CreateAccount();
        using var client = Client(token);
        (await client.GetAsync("/api/patient/medications")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rawReset = "SYNTHETIC-RESET-" + Guid.NewGuid();
        string challenge;
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            db.PasswordResetTokens.Add(new PasswordResetToken {
                Id = Guid.NewGuid(), UserId = user.Id, Email = user.Email,
                TokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawReset))),
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddMinutes(15), RequestIpAddress = "local-test", UserAgent = "test"
            });
            await db.SaveChangesAsync();
            challenge = await scope.ServiceProvider.GetRequiredService<ITwoFactorAuthService>().IssueChallenge(user.Id);
        }
        const string replacement = "Synthetic-Test!745Replacement";
        (await client.PostAsJsonAsync("/api/passwordreset/reset", new { token = rawReset, newPassword = replacement, confirmPassword = replacement }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertRevoked(client, token);
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ITwoFactorAuthService>().ValidateChallenge(user.Id, challenge)).Should().BeFalse();
        (await client.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = Password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = user.Username, password = replacement });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        (await client.GetAsync("/api/patient/medications")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("role")]
    [InlineData("active")]
    public async Task SecurityChangesInvalidateSessionsEvenWhenRoleOrStatusIsRestored(string change)
    {
        var (user, token) = await CreateAccount();
        using var client = Client(token);
        await MutateUser(user.Id, u => {
            if (change == "password") u.PasswordHash = "test-only-replaced-hash";
            if (change == "role") u.Role = UserRole.Doctor;
            if (change == "active") u.IsActive = false;
        });
        await AssertRevoked(client, token);
        await MutateUser(user.Id, u => { u.Role = UserRole.Patient; u.IsActive = true; });
        await AssertRevoked(client, token);
    }

    [Fact]
    public async Task OrdinaryProfileEditDoesNotInvalidateSession()
    {
        var (user, token) = await CreateAccount();
        using var client = Client(token);
        await MutateUser(user.Id, u => u.FirstName = "Updated synthetic name");
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token.RefreshToken })).StatusCode.Should().Be(HttpStatusCode.OK);
        // Rotating a refresh token preserves the active family, including the earlier access token.
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LogoutUsesAuthenticatedFamily_NotAnotherDevicesRefreshToken()
    {
        var (user, token) = await CreateAccount();
        TokenResponse other;
        using (var scope = factory.Services.CreateScope())
            other = (await scope.ServiceProvider.GetRequiredService<IJwtService>().GenerateSecurityToken(user.Username))!;
        using var client = Client(token);
        (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = other.RefreshToken, accessToken = other.AccessToken }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertRevoked(client, token);
        using var otherClient = Client(other);
        (await otherClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeletionClosesPrivateAccessAndAllPatientControlledSharing()
    {
        var (patient, token) = await CreateAccount();
        var (doctor, doctorToken) = await CreateAccount(UserRole.Doctor);
        using var client = Client(token);
        using var doctorClient = Client(doctorToken);
        var grantId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            db.EmergencyHealthProfiles.Add(new EmergencyHealthProfile {
                Id = Guid.NewGuid(), PatientId = patient.Id, IsSharingEnabled = true, EmergencyContacts = "[TEST] fictional contact"
            });
            db.EmergencyAccessGrants.Add(new EmergencyAccessGrant {
                Id = grantId, PatientId = patient.Id, RequesterId = doctor.Id, Reason = "[TEST] synthetic", ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            });
            for (var i = 0; i < 2; i++) db.Set<CareCircleInvitation>().Add(new CareCircleInvitation {
                Id = Guid.NewGuid(), PatientId = patient.Id, InviteeEmailNormalized = $"care-test-{i}@example.invalid",
                Role = CareCircleRole.Family, TokenHash = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddHours(1), AccessExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }
        var shareResponse = await client.PostAsync("/api/emergencyaccess/share-token", null);
        shareResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var share = await shareResponse.Content.ReadFromJsonAsync<JsonElement>();
        var shareToken = share.GetProperty("token").GetString();
        (await doctorClient.GetAsync($"/api/emergencyaccess/grants/{grantId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var deletion = await client.DeleteAsync($"/api/user/{patient.Id}");
        deletion.StatusCode.Should().Be(HttpStatusCode.OK, await deletion.Content.ReadAsStringAsync());
        await AssertRevoked(client, token);
        (await doctorClient.PostAsJsonAsync("/api/emergencyaccess/request", new { token = shareToken, reason = "[TEST] synthetic", facilityName = "[TEST] synthetic" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await doctorClient.GetAsync($"/api/emergencyaccess/grants/{grantId}")).StatusCode.Should().Be(HttpStatusCode.Gone);
        using var checkScope = factory.Services.CreateScope();
        var check = checkScope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        var profile = await check.EmergencyHealthProfiles.SingleAsync(p => p.PatientId == patient.Id);
        profile.IsSharingEnabled.Should().BeFalse();
        profile.ShareTokenHash.Should().BeNull();
        profile.EmergencyContacts.Should().BeNull();
        (await check.Set<CareCircleInvitation>().Where(i => i.PatientId == patient.Id).ToListAsync())
            .Should().OnlyContain(i => i.Status == CareCircleInvitationStatus.Revoked);
    }

    [Fact]
    public async Task QueryTokensWorkOnlyOnExplicitHubEndpoints()
    {
        var (_, token) = await CreateAccount();
        using var anonymous = factory.CreateClient();
        var query = "?access_token=" + Uri.EscapeDataString(token.AccessToken);
        (await anonymous.GetAsync("/notificationHub" + query)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "authentication reaches the hub transport protocol");
        (await anonymous.GetAsync("/api/auth/me" + query)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("valid", 201)]
    [InlineData("inactive-hospital", 404)]
    [InlineData("inactive-doctor", 404)]
    [InlineData("unaffiliated", 400)]
    [InlineData("expired-license", 400)]
    [InlineData("duration", 400)]
    [InlineData("not-accepting", 409)]
    public async Task BookingEnforcesParticipantAndFacilityEligibility(string scenario, int expected)
    {
        var (patient, patientToken) = await CreateAccount();
        var (doctor, doctorToken) = await CreateAccount(UserRole.Doctor);
        var hospitalId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            db.Hospitals.Add(new Hospital { Id = hospitalId, Name = "[TEST] isolated facility", Address = "Synthetic", City = "Baku", Country = "AZ", PhoneNumber = "", IsActive = scenario != "inactive-hospital" });
            var profile = await db.DoctorProfiles.SingleAsync(p => p.UserId == doctor.Id);
            profile.HospitalIds = scenario == "unaffiliated" ? [] : [hospitalId];
            if (scenario == "expired-license") profile.LicenseExpiryDate = DateTime.UtcNow.AddDays(-1);
            if (scenario == "not-accepting") profile.AcceptingNewPatients = false;
            if (scenario == "inactive-doctor") (await db.Users.FindAsync(doctor.Id))!.IsActive = false;
            await db.SaveChangesAsync();
        }
        // The valid case is made by the treating doctor for a different patient:
        // this used to be rejected by the caller-id / doctor-id comparison.
        using var client = Client(scenario == "valid" ? doctorToken : patientToken);
        var response = await client.PostAsJsonAsync("/api/appointment/book", new {
            patientId = patient.Id, doctorId = doctor.Id, hospitalId,
            scheduledAt = DateTime.UtcNow.AddDays(2), durationMinutes = scenario == "duration" ? 900 : 30,
            reason = "[TEST] synthetic booking"
        });
        ((int)response.StatusCode).Should().Be(expected, await response.Content.ReadAsStringAsync());
    }
}
