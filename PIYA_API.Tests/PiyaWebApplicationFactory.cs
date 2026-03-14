using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Service.Interface;
using PIYA_API.Service.Class;

namespace PIYA_API.Tests;

/// <summary>
/// A shared WebApplicationFactory for all test classes.
///
/// Configuration strategy (no appsettings.json required):
///   - Does NOT clear existing config sources so that appsettings.Development.json
///     (or CI env-vars) still supply the JWT signing key that Program.cs captures
///     at host-build time before ConfigureAppConfiguration callbacks run.
///   - Adds an in-memory layer on top to override the DB connection string,
///     feature flags, and other test-specific settings without touching secrets.
///   - Always uses the real PostgreSQL database — never EF InMemory.
/// </summary>
public class PiyaWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs validates Jwt:SecretKey during host construction, before test-time
        // ConfigureAppConfiguration overrides are applied. Seed valid env-vars here so
        // the host can build deterministically in local/CI test runs.
        Environment.SetEnvironmentVariable("Jwt__SecretKey", "PIYA_LOCAL_TEST_JWT_SECRET_KEY_AT_LEAST_32_CHARS_LONG");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "PIYA_API_Test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "PIYA_Clients_Test");
        Environment.SetEnvironmentVariable("Security__QrSigningKey", "PIYA_LOCAL_TEST_QR_SIGNING_KEY_AT_LEAST_32_CHARS_LONG");

        // ----------------------------------------------------------------
        // Add a high-priority in-memory config layer.
        // This runs AFTER the host is built, so it cannot change values
        // that Program.cs already captured (e.g. the JWT signing key read
        // during AddAuthentication).  Use it to override things that are
        // resolved at request time: connection strings, feature flags, etc.
        // ----------------------------------------------------------------
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Layer our test overrides on top — do NOT clear existing sources.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Point at the developer's local Postgres DB (same DB the API uses).
                // CI overrides this via the ConnectionStrings__DefaultConnection env-var.
                ["ConnectionStrings:DefaultConnection"] =
                    GetEnvOrDefault(
                        "ConnectionStrings__DefaultConnection",
                        "Host=localhost;Database=piya_db;Username=mahammadbbyv;Pooling=true;MaxPoolSize=10;Timeout=10"),

                // Disable features that depend on external services or slow things down.
                ["Features:EnableAuditLogging"]       = "false",
                ["Features:EnableRateLimiting"]       = "false",
                ["Features:EnableCaching"]            = "false",
                ["Features:EnableTwoFactorAuth"]      = "false",
                ["Features:EnableQrCodeSystem"]       = "true",
                ["Features:EnableAppointmentSystem"]  = "true",
                ["Features:EnablePrescriptionSystem"] = "true",

                // Use in-memory distributed cache (no Redis required).
                ["Caching:Provider"] = "InMemory",

                // Keep test logs quiet.
                ["Logging:LogLevel:Default"]                       = "Warning",
                ["Logging:LogLevel:Microsoft.AspNetCore"]          = "Error",
                ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
            });

            // If CI injects Jwt / Security keys via env-vars, expose them
            // under the colon-separated keys so they override appsettings.
            MaybeOverride(config, "Jwt:SecretKey",          "Jwt__SecretKey");
            MaybeOverride(config, "Jwt:Issuer",             "Jwt__Issuer");
            MaybeOverride(config, "Jwt:Audience",           "Jwt__Audience");
            MaybeOverride(config, "Security:QrSigningKey",  "Security__QrSigningKey");
        });

        // Run under "LoadTest" — matches ASPNETCORE_ENVIRONMENT in CI and means
        // appsettings.LoadTest.json (which exists) is loaded instead of the
        // missing appsettings.Test.json that used to cause Serilog to crash
        // the host before any test could run.
        builder.UseEnvironment("LoadTest");

        // Replace the Singleton SecurityHardeningService with a fresh instance so
        // that failed-login counters and lockout state do NOT bleed between tests.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISecurityHardeningService>();
            services.AddSingleton<ISecurityHardeningService, SecurityHardeningService>();
        });
    }

    /// <summary>
    /// Ensures pending EF migrations are applied to the test database before any
    /// test runs.  Called lazily by tests that need a fully-migrated schema.
    /// </summary>
    public async Task EnsureMigratedAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// If the given env-var is set, add it as a config override so it takes
    /// precedence over any appsettings file value.
    /// </summary>
    private static void MaybeOverride(IConfigurationBuilder config, string configKey, string envVar)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(value))
            config.AddInMemoryCollection(new Dictionary<string, string?> { [configKey] = value });
    }

    protected static string GetEnvOrDefault(string envVarName, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(envVarName);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}
