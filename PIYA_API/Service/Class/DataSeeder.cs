using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        // Batch-preload all demo users matched by either username or email
        // so that rows created with an old email are still found and reconciled.
        var demoEmails    = DemoUsers.Select(u => u.Email).ToArray();
        var demoUsernames = DemoUsers.Select(u => u.Username).ToArray();

        var existing = await db.Users
            .Where(u => demoEmails.Contains(u.Email) || demoUsernames.Contains(u.Username))
            .ToDictionaryAsync(u => u.Username);

        foreach (var (username, first, last, email, phone, role) in DemoUsers)
        {
            if (!existing.TryGetValue(username, out var user))
            {
                // Not in DB yet — create and track in both EF and our local dict.
                user = new User
                {
                    Id              = Guid.NewGuid(),
                    Email           = email,
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
                existing[username] = user;
            }

            // Always reconcile ALL mutable fields — including email — so repeated
            // runs correct rows that were created with an old/different email.
            user.Username    = username;
            user.Email       = email;
            user.FirstName   = first;
            user.LastName    = last;
            user.PhoneNumber = phone;
            user.Role        = role;
            user.IsActive        = true;
            user.IsEmailVerified = true;
            user.UpdatedAt   = DateTime.UtcNow;

            if (!hasher.VerifyPassword(DemoPassword, user.PasswordHash))
                user.PasswordHash = DemoHash;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            // Another instance seeded concurrently (e.g. parallel CI jobs).
            // The row already exists — swallow and continue.
            logger.LogWarning(ex,
                "[DataSeeder] Unique-constraint conflict while seeding demo users — " +
                "a concurrent startup already inserted the row. Continuing.");
        }

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
