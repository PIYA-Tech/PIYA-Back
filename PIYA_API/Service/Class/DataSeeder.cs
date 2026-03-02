using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Seeds demo/development users so the login page works out of the box.
/// Password for all demo accounts: Test@1234
/// On every startup it ensures the hash is correct (idempotent).
/// </summary>
public static class DataSeeder
{
    private const string DemoPassword = "Test@1234";
    // Pre-computed BCrypt hash of "Test@1234" (cost 11) — avoids slow hashing on every startup check
    private const string DemoHash = "$2a$11$MM/G.xABJSIlt/lfqsiu9O8/wKvF4sk31Pl9tL7YJ.tEu0Gpp6gR6";

    private static readonly (string Username, string First, string Last, string Email, string Phone, UserRole Role)[] DemoUsers =
    [
        ("mahammad_babayev", "Mahammad", "Babayev", "mahammad_babayev@piya.dev", "+994501000001", UserRole.Patient),
        ("dr_at_piya", "Dr", "Piya", "dr_at_piya@piya.dev", "+994501000002", UserRole.Doctor),
        ("pharma_piya", "Pharma", "Piya", "pharma_piya@piya.dev", "+994501000003", UserRole.Pharmacist),
        ("admin_piya", "Admin", "PIYA", "admin_piya@piya.dev", "+994501000004", UserRole.Admin),
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        foreach (var (username, first, last, email, phone, role) in DemoUsers)
        {
            var existing = await db.Users.SingleOrDefaultAsync(u => u.Username == username);

            if (existing != null)
            {
                // Ensure demo password is always correct (idempotent)
                if (!hasher.VerifyPassword(DemoPassword, existing.PasswordHash))
                {
                    existing.PasswordHash = DemoHash;
                    existing.UpdatedAt    = DateTime.UtcNow;
                }
                continue;
            }

            // User doesn't exist — create fresh
            db.Users.Add(new User
            {
                Id          = Guid.NewGuid(),
                Username    = username,
                FirstName   = first,
                LastName    = last,
                Email       = email,
                PhoneNumber = phone,
                DateOfBirth = new DateTime(1990, 1, 1),
                Role        = role,
                PasswordHash     = DemoHash,
                IsActive         = true,
                IsEmailVerified  = true,
                IsPhoneVerified  = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TokensInfo = new Token
                {
                    Id           = Guid.NewGuid(),
                    AccessToken  = string.Empty,
                    RefreshToken = string.Empty,
                    ExpiresAt    = DateTime.UtcNow,
                    DeviceInfo   = "Seed",
                },
            });
        }

        await db.SaveChangesAsync();
    }
}
