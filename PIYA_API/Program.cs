using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Extensions;
using PIYA_API.Service.Class;
using Serilog;
using FluentValidation;
using FluentValidation.AspNetCore;
using Asp.Versioning;
using System.Reflection;

// Contract generators must build the full endpoint graph, but must never migrate
// or seed a database. The entry-assembly check covers Microsoft's build-time
// generator; the explicit flag covers the pinned Swashbuckle fallback used while
// .NET 9's generator cannot resolve this app's recursive legacy response models.
var isOpenApiGeneration =
    string.Equals(
        Assembly.GetEntryAssembly()?.GetName().Name,
        "GetDocument.Insider",
        StringComparison.Ordinal)
    || string.Equals(
        Environment.GetEnvironmentVariable("PIYA_GENERATE_OPENAPI"),
        "true",
        StringComparison.OrdinalIgnoreCase);

// Configure Serilog (do not require appsettings.json at startup)
var bootstrapConfig = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(bootstrapConfig)
    // Hosting's information-level request logs include the query string.
    // SignalR browsers send JWTs there; never write these credentials to logs.
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/piya-api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting PIYA Healthcare API");

    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.ApplyDeploymentEnvironmentAliases();

    // Use Serilog for logging
    builder.Host.UseSerilog();

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    // FluentValidation
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // API Versioning
    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    });

    builder.Services.AddOpenApi();
    builder.Services.AddHttpClient();

    // ── Infrastructure ──────────────────────────────────────────────────
    builder.Services.AddPiyaConfiguration(builder.Configuration, builder.Environment);
    builder.Services.AddPiyaCaching(builder.Configuration);
    builder.Services.AddPiyaDataProtection(builder.Configuration);
    builder.Services.AddPiyaDatabase(builder.Configuration, builder.Environment);
    builder.Services.AddPiyaCors(builder.Configuration);
    builder.Services.AddPiyaSignalR(builder.Configuration);
    builder.Services.AddPiyaSwagger(builder.Environment);

    // ── Authentication & Authorization ──────────────────────────────────
    builder.Services.AddPiyaAuthentication(builder.Configuration);
    builder.Services.AddPiyaAuthorization();

    // ── Application services (DI) ───────────────────────────────────────
    builder.Services.AddPiyaApplicationServices(builder.Configuration);

    var app = builder.Build();

    // ── Startup tasks ───────────────────────────────────────────────────
    if (!isOpenApiGeneration)
    {
        var migrateOnly = app.Configuration.GetValue<bool>("Database:MigrateOnly");
        // Direct Docker/Coolify deployments do not include the ignored local
        // appsettings files. Default to applying migrations unless an operator
        // explicitly opts out because a separate migration job owns the schema.
        var autoMigrate = app.Configuration.GetValue<bool?>("Database:AutoMigrate") ?? true;
        Log.Information(
            "Database startup mode: AutoMigrate={AutoMigrate}, MigrateOnly={MigrateOnly}",
            autoMigrate,
            migrateOnly);
        if (migrateOnly || autoMigrate)
        {
            using var migrationScope = app.Services.CreateScope();
            var db = migrationScope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToArray();
            Log.Information(
                "Applying {MigrationCount} pending database migration(s): {PendingMigrations}",
                pendingMigrations.Length,
                pendingMigrations.Length == 0 ? "none" : string.Join(", ", pendingMigrations));
            await db.Database.MigrateAsync();
        }

        if (migrateOnly)
        {
            Log.Information("Database migrations completed; exiting migration-only process");
            await app.DisposeAsync();
            return;
        }

        // Seed production super-admin from SuperAdmin:* config / SuperAdmin__* env vars.
        // Integration tests explicitly opt out and create their own isolated fixture user.
        if (!app.Configuration.GetValue<bool>("TestHarness:SkipProductionSeeding"))
            await ProductionSeeder.SeedAsync(app.Services);

        // Seed demo users when explicitly enabled — blocked in Production for safety.
        var seedEnabled =
            string.Equals(Environment.GetEnvironmentVariable("ENABLE_DEMO_SEEDING"), "true", StringComparison.OrdinalIgnoreCase)
            || app.Configuration.GetValue<bool>("DemoSeeding:Enabled");
        if (seedEnabled && app.Environment.IsProduction())
        {
            Log.Warning("ENABLE_DEMO_SEEDING is set in Production — ignoring for safety. " +
                        "Remove the env-var or set DemoSeeding:Enabled=false.");
        }
        else if (seedEnabled)
        {
            await DataSeeder.SeedAsync(app.Services);
        }
    }

    // ── Middleware pipeline ──────────────────────────────────────────────
    app.UsePiyaMiddleware();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw; // Migration/startup failures must fail the container process.
}
finally
{
    Log.CloseAndFlush();
}

// Make the implicit Program class public for integration testing
public partial class Program { }
