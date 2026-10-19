using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests;

/// <summary>Creates an explicit verified fixture identity, then exercises real login.
/// Authorization tests must not rely on public registration assigning staff roles.</summary>
internal static class TestIdentity
{
    public static async Task<string> CreateAndLoginAsync(PiyaWebApplicationFactory factory, HttpClient client, UserRole role)
    {
        await factory.EnsureMigratedAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        const string password = "SyntheticTest@Password123";
        var id = Guid.NewGuid();
        var user = new User { Id = id, Username = "fixture_" + id.ToString("N"), Email = id + "@example.test",
            FirstName = "Synthetic", LastName = "Fixture", PhoneNumber = "", IsActive = true, IsEmailVerified = true, Role = role,
            PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().HashPassword(password) };
        db.Users.Add(user); await db.SaveChangesAsync();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { identifier = user.Email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token), "Fixture login must produce a usable token.");
        return token!;
    }
}
