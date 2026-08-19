using Microsoft.EntityFrameworkCore;
using Npgsql;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Seeds data that MUST exist in every environment including production:
///   1. SuperAdmin user — credentials come from config/env vars, never hardcoded.
///   2. Medications     — imported from the Azerbaijan Pharmaceutical Registry on
///                        first run (when the medications table is empty).
///
/// All operations are fully idempotent — safe to run on every startup.
/// The SuperAdmin password is read from:
///   env var  : SuperAdmin__Password          (recommended for production)
///   appsettings: SuperAdmin:Password        (fallback — do not commit real values)
/// </summary>
public static class ProductionSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db      = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        var hasher  = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var config  = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger  = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        await SeedSuperAdminAsync(db, hasher, config, logger);
        await SeedMedicationsAsync(db, services, config, logger);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SuperAdmin
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task SeedSuperAdminAsync(
        PharmacyApiDbContext db,
        IPasswordHasher hasher,
        IConfiguration config,
        ILogger logger)
    {
        // Read credentials — env var takes priority over appsettings so that
        // Docker / cloud deployments can inject secrets without touching files.
        var username  = config["SuperAdmin:Username"];
        var email     = config["SuperAdmin:Email"];
        var firstName = config["SuperAdmin:FirstName"];
        var lastName  = config["SuperAdmin:LastName"];
        var phone     = config["SuperAdmin:Phone"];
        var password  = config["SuperAdmin:Password"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "[ProductionSeeder] SuperAdmin credentials are not fully configured — " +
                "set the corresponding env vars or SuperAdmin settings in appsettings. " +
                "SuperAdmin account will NOT be created/updated.");
            return;
        }

        // Find by username OR email so a rename doesn't duplicate the row.
        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Username == username || u.Email == email);

        if (existing == null)
        {
            var newAdmin = new User
            {
                Id              = Guid.NewGuid(),
                Username        = username,
                Email           = email,
                FirstName       = firstName,
                LastName        = lastName,
                PhoneNumber     = phone,
                Role            = UserRole.SuperAdmin,
                PasswordHash    = hasher.HashPassword(password),
                DateOfBirth     = new DateTime(1990, 1, 1),
                IsActive        = true,
                IsEmailVerified = true,
                IsPhoneVerified = true,
                CreatedAt       = DateTime.UtcNow,
                UpdatedAt       = DateTime.UtcNow,
            };

            db.Users.Add(newAdmin);

            try
            {
                await db.SaveChangesAsync();
                logger.LogInformation(
                    "[ProductionSeeder] SuperAdmin created — username: {Username}, email: {Email}",
                    username, email);
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
            {
                // Concurrent startup race — already inserted by another instance.
                logger.LogWarning(
                    "[ProductionSeeder] SuperAdmin unique-constraint conflict (concurrent insert) — skipping.");
            }
        }
        else
        {
            // Account exists — reconcile mutable fields and always re-hash the
            // password so a credential rotation takes effect on next deploy.
            bool changed = false;

            if (existing.Role != UserRole.SuperAdmin)
            { existing.Role = UserRole.SuperAdmin; changed = true; }

            if (existing.Username != username)
            { existing.Username = username; changed = true; }

            if (existing.Email != email)
            { existing.Email = email; changed = true; }

            if (!hasher.VerifyPassword(password, existing.PasswordHash))
            { existing.PasswordHash = hasher.HashPassword(password); changed = true; }

            if (!existing.IsActive)
            { existing.IsActive = true; changed = true; }

            if (!existing.IsEmailVerified)
            { existing.IsEmailVerified = true; changed = true; }

            if (changed)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                logger.LogInformation(
                    "[ProductionSeeder] SuperAdmin updated — username: {Username}", username);
            }
            else
            {
                logger.LogInformation(
                    "[ProductionSeeder] SuperAdmin already up-to-date — username: {Username}", username);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Medications
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task SeedMedicationsAsync(
        PharmacyApiDbContext db,
        IServiceProvider services,
        IConfiguration config,
        ILogger logger)
    {
        var registryEnabled = config.GetValue<bool>("ExternalApis:AzerbaijanPharmaceuticalRegistry:Enabled", true);
        if (!registryEnabled)
        {
            logger.LogInformation("[ProductionSeeder] Azerbaijan registry disabled — skipping medication seed.");
            return;
        }

        var count = await db.Medications.CountAsync();
        if (count > 0)
        {
            logger.LogInformation(
                "[ProductionSeeder] {Count} medications already in DB — skipping registry sync.", count);
            return;
        }

        logger.LogInformation(
            "[ProductionSeeder] No medications found — importing from Azerbaijan Pharmaceutical Registry...");

        try
        {
            await using var scope = services.CreateAsyncScope();
            var registryService = scope.ServiceProvider
                .GetRequiredService<IAzerbaijanPharmaceuticalRegistryService>();

            var result = await registryService.SyncMedicationsAsync();

            if (result.Success)
                logger.LogInformation(
                    "[ProductionSeeder] Registry sync complete — {Total} records imported ({New} new).",
                    result.TotalRecords, result.NewRecords);
            else
                logger.LogWarning(
                    "[ProductionSeeder] Registry sync finished with errors: {Errors}",
                    string.Join("; ", result.Errors));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ProductionSeeder] Registry sync threw an exception.");
        }
    }
}
