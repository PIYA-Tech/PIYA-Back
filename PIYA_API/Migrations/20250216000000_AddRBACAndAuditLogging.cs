using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PIYA_API.Data;

#nullable disable

namespace PIYA_API.Migrations;

/// <summary>
/// Historical compatibility marker. The original hand-written operations were
/// never discoverable and are fully represented by the generated
/// 20260217103131_AddInventoryBatchAndHistory migration.
/// </summary>
[DbContext(typeof(PharmacyApiDbContext))]
[Migration("20250216000000_AddRBACAndAuditLogging")]
public partial class AddRBACAndAuditLogging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
