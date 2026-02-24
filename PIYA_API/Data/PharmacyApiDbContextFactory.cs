using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PIYA_API.Data;

/// <summary>
/// Design-time factory for PharmacyApiDbContext
/// Used by EF Core tools (migrations, database update) at design time
/// </summary>
public class PharmacyApiDbContextFactory : IDesignTimeDbContextFactory<PharmacyApiDbContext>
{
    public PharmacyApiDbContext CreateDbContext(string[] args)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var projectDir = Path.Combine(currentDir, "PIYA_API");

        // Build configuration with optional files + environment variables.
        // This allows CI to run purely with env vars and without committed secrets.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(currentDir)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.LoadTest.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine("PIYA_API", "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine("PIYA_API", "appsettings.Development.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine("PIYA_API", "appsettings.LoadTest.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine("PIYA_API", "appsettings.Production.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        // Get connection string from configuration
        var connectionString =
            configuration.GetConnectionString("DefaultConnection") ??
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'DefaultConnection' not found. " +
                $"Checked env var ConnectionStrings__DefaultConnection and optional appsettings files. " +
                $"Current directory: {currentDir}; Project directory exists: {Directory.Exists(projectDir)}");
        }

        // Build DbContext options
        var optionsBuilder = new DbContextOptionsBuilder<PharmacyApiDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new PharmacyApiDbContext(optionsBuilder.Options);
    }
}
