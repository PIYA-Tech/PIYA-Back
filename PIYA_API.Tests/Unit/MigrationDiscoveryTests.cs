using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class MigrationDiscoveryTests
{
    [Fact]
    public void MigrationAssembly_IncludesUtcTimestampConversion()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new PharmacyApiDbContext(options);

        context.Database.GetMigrations()
            .Should().Contain("20260328120000_MigrateToUtcTimestamps");
    }
}
