using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Service.Interface;
using PIYA_API.Service.Class;
using PIYA_API.Model;
using Npgsql;
using System.Text.RegularExpressions;

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
    public const string IntegrationAdminUsername = "piya_integration_admin";
    public const string IntegrationAdminPassword = "IntegrationAdmin@123";

    private static readonly SemaphoreSlim DatabaseSetupGate = new(1, 1);
    private readonly string _testConnection = ValidateTestConnection(
        Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
    private bool _databaseReady;

    public static string ValidateTestConnection(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Tests require an explicit ConnectionStrings__DefaultConnection pointing to a disposable *_test or *_audit database. No application database fallback is allowed.");
        var parsed = new NpgsqlConnectionStringBuilder(connection);
        if (parsed.Database is null || !Regex.IsMatch(parsed.Database, @"(?:^|_)(?:test|tests|audit)(?:_|$)", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Refusing to run tests: the explicit database name must contain a separate test, tests or audit segment. Never point tests at the PIYA application database.");
        return connection;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Program.cs validates Jwt:SecretKey during host construction, before test-time
        // ConfigureAppConfiguration overrides are applied. Seed valid env-vars here so
        // the host can build deterministically in local/CI test runs.
        Environment.SetEnvironmentVariable("Jwt__SecretKey", "PIYA_LOCAL_TEST_JWT_SECRET_KEY_AT_LEAST_32_CHARS_LONG");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "PIYA_API_Test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "PIYA_Clients_Test");
        Environment.SetEnvironmentVariable("Security__QrSigningKey", "PIYA_LOCAL_TEST_QR_SIGNING_KEY_AT_LEAST_32_CHARS_LONG");
        Environment.SetEnvironmentVariable("Security__PrescriptionSigningKey", "PIYA_LOCAL_TEST_PRESCRIPTION_SIGNING_KEY_32_CHARS_LONG");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "http://localhost");
        Environment.SetEnvironmentVariable("ENABLE_DEMO_SEEDING", "false");
        Environment.SetEnvironmentVariable("TestHarness__SkipProductionSeeding", "true");
        Environment.SetEnvironmentVariable("FacilityDirectory__Enabled", "false");
        Environment.SetEnvironmentVariable("Firebase__Enabled", "false");
        Environment.SetEnvironmentVariable("ApplePush__Enabled", "false");
        Environment.SetEnvironmentVariable(
            "DataProtection__KeyPath",
            Path.Combine(Path.GetTempPath(), "piya-test-data-protection"));

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
                ["ConnectionStrings:DefaultConnection"] = _testConnection,

                // Disable features that depend on external services or slow things down.
                ["Features:EnableAuditLogging"]       = "false",
                ["Features:EnableRateLimiting"]       = "false",
                ["Features:EnableCaching"]            = "false",
                ["Features:EnableTwoFactorAuth"]      = "false",
                ["Features:EnableQrCodeSystem"]       = "true",
                ["Features:EnableAppointmentSystem"]  = "true",
                ["Features:EnablePrescriptionSystem"] = "true",

                // Test hosts must never validate or contact live delivery providers.
                ["ExternalApis:EmailService:Enabled"] = "false",
                ["ExternalApis:SmsService:Enabled"] = "false",

                // Disable rate limiting globally so integration tests don't get throttled.
                ["RateLimiting:EnableGlobal"] = "false",

                // CI supplies Redis and therefore exercises the distributed cache.
                // Local runs remain self-contained unless Redis is explicitly selected.
                ["Caching:Provider"] =
                    GetEnvOrDefault("Caching__Provider", "InMemory"),
                ["Caching:RedisConnectionString"] =
                    GetEnvOrDefault("Caching__RedisConnectionString", ""),

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

            // Integration tests must never contact a real SMTP server.
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService, NoOpEmailService>();
        });
    }

    /// <summary>
    /// Ensures pending EF migrations are applied to the test database before any
    /// test runs.  Called lazily by tests that need a fully-migrated schema.
    /// </summary>
    public async Task EnsureMigratedAsync()
    {
        await DatabaseSetupGate.WaitAsync();
        try
        {
            if (_databaseReady)
                return;

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            await db.Database.MigrateAsync();

            // Appointment tests require an administrator but should not depend on
            // production/demo startup seeders or fixed global database contents.
            var admin = await db.Users.FirstOrDefaultAsync(
                user => user.Username == IntegrationAdminUsername);
            if (admin == null)
            {
                admin = new User
                {
                    Id = Guid.NewGuid(),
                    Username = IntegrationAdminUsername,
                    Email = "piya-integration-admin@example.test",
                    FirstName = "Integration",
                    LastName = "Admin",
                    PhoneNumber = "+15555550199",
                    Role = UserRole.Admin,
                    IsActive = true,
                    IsEmailVerified = true,
                    IsPhoneVerified = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Users.Add(admin);
            }

            admin.Role = UserRole.Admin;
            admin.IsActive = true;
            // Multiple factories share the test admin. Rehashing on every
            // fixture would invalidate sessions still in use by another test.
            if (string.IsNullOrEmpty(admin.PasswordHash) || !passwordHasher.VerifyPassword(IntegrationAdminPassword, admin.PasswordHash))
                admin.PasswordHash = passwordHasher.HashPassword(IntegrationAdminPassword);
            admin.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            _databaseReady = true;
        }
        finally
        {
            DatabaseSetupGate.Release();
        }
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

    private sealed class NoOpEmailService : IEmailService
    {
        public Task SendEmailVerificationAsync(
            string toEmail,
            string userName,
            string verificationToken,
            string verificationUrl) => Task.CompletedTask;

        public Task SendPasswordResetAsync(
            string toEmail,
            string userName,
            string resetToken,
            string resetUrl) => Task.CompletedTask;

        public Task SendAppointmentConfirmationAsync(
            string toEmail,
            string patientName,
            DateTime appointmentDate,
            string doctorName,
            string hospitalName) => Task.CompletedTask;

        public Task SendAppointmentCancelledAsync(
            string toEmail,
            string patientName,
            DateTime appointmentDate,
            string doctorName,
            string cancelledBy,
            string? reason) => Task.CompletedTask;

        public Task SendAppointmentRescheduledAsync(
            string toEmail,
            string patientName,
            DateTime oldDate,
            DateTime newDate,
            string doctorName) => Task.CompletedTask;

        public Task SendAppointmentReminderAsync(
            string toEmail,
            string patientName,
            DateTime appointmentDate,
            string doctorName) => Task.CompletedTask;

        public Task SendPrescriptionReadyAsync(
            string toEmail,
            string patientName,
            string pharmacyName) => Task.CompletedTask;

        public Task Send2FACodeAsync(string toEmail, string code) =>
            Task.CompletedTask;

        public Task SendEmailAsync(
            string toEmail,
            string subject,
            string htmlBody,
            string? plainTextBody = null) => Task.CompletedTask;
    }
}
