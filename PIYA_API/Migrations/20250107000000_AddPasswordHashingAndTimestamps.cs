using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PIYA_API.Data;

#nullable disable

namespace PIYA_API.Migrations;

/// <summary>
/// Historical compatibility marker.
///
/// This migration was originally committed without EF metadata and therefore
/// never participated in the migration chain. Its schema operations were later
/// incorporated into 20260217103131_AddInventoryBatchAndHistory. Keeping this
/// migration as a discoverable no-op lets existing databases record the missing
/// history row without replaying duplicate columns, while fresh databases still
/// receive the changes from the generated 202602 migration.
/// </summary>
[DbContext(typeof(PharmacyApiDbContext))]
[Migration("20250107000000_AddPasswordHashingAndTimestamps")]
public partial class AddPasswordHashingAndTimestamps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
