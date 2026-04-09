using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Extensions;
using PIYA_API.Service.Class;
using Serilog;
using FluentValidation;
using FluentValidation.AspNetCore;
using Asp.Versioning;

// Configure Serilog (do not require appsettings.json at startup)
var bootstrapConfig = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(bootstrapConfig)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/piya-api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting PIYA Healthcare API");

    var builder = WebApplication.CreateBuilder(args);

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
    builder.Services.AddPiyaConfiguration(builder.Configuration);
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

    // ── Middleware pipeline ──────────────────────────────────────────────
    app.UsePiyaMiddleware();

    // ── Startup tasks ───────────────────────────────────────────────────
    // Apply pending EF Core migrations on startup (creates tables if needed).
    using (var migrationScope = app.Services.CreateScope())
    {
        var db = migrationScope.ServiceProvider.GetRequiredService<PharmacyApiDbContext>();
        await db.Database.MigrateAsync();
    }

    // Seed production super-admin from SuperAdmin:* config / PIYA__SuperAdmin__* env vars.
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

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make the implicit Program class public for integration testing
public partial class Program { }
