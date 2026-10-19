using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class SeparateReportedAndBatchStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LegacyQuantityInStock",
                table: "PharmacyInventories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportedAt",
                table: "PharmacyInventories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportedQuantity",
                table: "PharmacyInventories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReportedSourceId",
                table: "PharmacyInventories",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresReconciliation",
                table: "PharmacyInventories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "PharmacyInventories",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "LegacyQuantity",
                table: "InventoryBatches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventorySyncReceipt",
                columns: table => new
                {
                    PharmacyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySyncReceipt", x => new { x.PharmacyId, x.RequestId });
                });

            migrationBuilder.CreateTable(
                name: "InventorySyncSource",
                columns: table => new
                {
                    PharmacyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastRevision = table.Column<long>(type: "bigint", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySyncSource", x => new { x.PharmacyId, x.SourceId });
                });

            // Preserve the original reported totals verbatim. Legacy counts are
            // not evidence of a manufactured lot or its expiry date.
            migrationBuilder.Sql("""
                UPDATE "PharmacyInventories"
                SET "LegacyQuantityInStock" = "QuantityInStock",
                    "ReportedQuantity" = CASE WHEN "QuantityInStock" >= 0 THEN "QuantityInStock" ELSE NULL END,
                    "RequiresReconciliation" = true;
                UPDATE "InventoryBatches"
                SET "LegacyQuantity" = "Quantity", "Quantity" = 0, "IsActive" = false
                WHERE "Quantity" < 0;
                UPDATE "PharmacyInventories" AS i
                SET "QuantityInStock" = CASE WHEN i."IsAvailable" THEN
                    COALESCE((SELECT SUM(b."Quantity") FROM "InventoryBatches" b
                        WHERE b."PharmacyInventoryId" = i."Id" AND b."IsActive" AND b."Quantity" > 0
                        AND length(trim(b."BatchNumber")) > 0 AND b."ExpirationDate" > now()), 0) ELSE 0 END;
                UPDATE "PharmacyInventories" AS i
                SET "RequiresReconciliation" =
                    i."LegacyQuantityInStock" <> i."QuantityInStock"
                    OR EXISTS (SELECT 1 FROM "InventoryBatches" b WHERE b."PharmacyInventoryId" = i."Id"
                        AND (b."LegacyQuantity" IS NOT NULL OR b."IsActive" AND
                            (b."ExpirationDate" IS NULL OR length(trim(b."BatchNumber")) = 0)))
                    OR (SELECT COUNT(*) FROM "PharmacyInventories" duplicate
                        WHERE duplicate."PharmacyId" = i."PharmacyId" AND duplicate."MedicationId" = i."MedicationId") > 1;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Inventory_Quantity_Nonnegative",
                table: "PharmacyInventories",
                sql: "\"QuantityInStock\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Inventory_Reported_Nonnegative",
                table: "PharmacyInventories",
                sql: "\"ReportedQuantity\" IS NULL OR \"ReportedQuantity\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryBatch_Quantity_Nonnegative",
                table: "InventoryBatches",
                sql: "\"Quantity\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // An automatic downgrade would discard reported provenance, receipts
            // and reconciled legacy evidence and revive the old dispensing model.
            throw new NotSupportedException("Restore a verified pre-migration backup for rollback; inventory separation cannot be downgraded without losing evidence.");
        }
    }
}
