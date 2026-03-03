using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Seeds demo/development data so the app works out of the box.
/// All operations are idempotent — safe to run on every startup.
/// Medications are imported from the Azerbaijan Pharmaceutical Registry on first run.
/// </summary>
public static class DataSeeder
{
    private const string DemoPassword = "Test@1234";
    private const string DemoHash = "$2a$11$MM/G.xABJSIlt/lfqsiu9O8/wKvF4sk31Pl9tL7YJ.tEu0Gpp6gR6";

    private static readonly (string Username, string First, string Last, string Email, string Phone, UserRole Role)[] DemoUsers =
    [
        ("mahammad_babayev", "Mahammad", "Babayev", "mahammad_babayev@piya.dev", "+994501000001", UserRole.Patient),
        ("dr_at_piya",       "Dr",       "Piya",    "dr_at_piya@piya.dev",       "+994501000002", UserRole.Doctor),
        ("pharma_piya",      "Pharma",   "Piya",    "pharma_piya@piya.dev",      "+994501000003", UserRole.Pharmacist),
        ("admin_piya",       "Admin",    "PIYA",    "admin_piya@piya.dev",        "+994501000004", UserRole.Admin),
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db     = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // ── Users ──────────────────────────────────────────────────────────────
        foreach (var (username, first, last, email, phone, role) in DemoUsers)
        {
            // Use email as the canonical lookup key (it has the unique index).
            // This survives re-runs, username renames, and test-created collisions.
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                user = new User
                {
                    Id              = Guid.NewGuid(),
                    Email           = email,
                    // Required members — overwritten by the reconcile block below
                    Username        = username,
                    FirstName       = first,
                    LastName        = last,
                    PhoneNumber     = phone,
                    Role            = role,
                    PasswordHash    = DemoHash,
                    DateOfBirth     = new DateTime(1990, 1, 1),
                    IsActive        = true,
                    IsEmailVerified = true,
                    IsPhoneVerified = true,
                    CreatedAt       = DateTime.UtcNow,
                    UpdatedAt       = DateTime.UtcNow,
                    TokensInfo = new Token
                    {
                        Id           = Guid.NewGuid(),
                        AccessToken  = string.Empty,
                        RefreshToken = string.Empty,
                        ExpiresAt    = DateTime.UtcNow,
                        DeviceInfo   = "Seed",
                    },
                };
                db.Users.Add(user);
            }

            // Always reconcile mutable fields so repeated runs stay consistent.
            user.Username    = username;
            user.FirstName   = first;
            user.LastName    = last;
            user.PhoneNumber = phone;
            user.Role        = role;
            user.UpdatedAt   = DateTime.UtcNow;

            if (!hasher.VerifyPassword(DemoPassword, user.PasswordHash))
                user.PasswordHash = DemoHash;
        }
        await db.SaveChangesAsync();

        // ── Medications from Azerbaijan Pharmaceutical Registry ─────────────────
        // Only runs when the DB is empty — subsequent startups skip this entirely.
        var count = await db.Medications.CountAsync();
        if (count == 0)
        {
            logger.LogInformation("[DataSeeder] No medications found — importing from Azerbaijan Pharmaceutical Registry...");
            try
            {
                var registryService = scope.ServiceProvider
                    .GetRequiredService<IAzerbaijanPharmaceuticalRegistryService>();

                var result = await registryService.SyncMedicationsAsync();
                if (result.Success)
                    logger.LogInformation("[DataSeeder] Registry sync complete — {Total} records imported ({New} new)",
                        result.TotalRecords, result.NewRecords);
                else
                    logger.LogWarning("[DataSeeder] Registry sync failed: {Errors}",
                        string.Join("; ", result.Errors));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DataSeeder] Registry sync threw an exception");
            }
        }
        else
        {
            logger.LogInformation("[DataSeeder] {Count} medications already in DB — skipping registry sync", count);
        }
    }
}
