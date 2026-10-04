using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class VisitCaseAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "AdmissionGrantId",
                table: "ClinicalCases",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "AdmissionAppointmentId",
                table: "ClinicalCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalCases_AdmissionAppointmentId",
                table: "ClinicalCases",
                column: "AdmissionAppointmentId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClinicalCases_AdmissionSource",
                table: "ClinicalCases",
                sql: "(\"AdmissionGrantId\" IS NOT NULL AND \"AdmissionAppointmentId\" IS NULL) OR (\"AdmissionGrantId\" IS NULL AND \"AdmissionAppointmentId\" IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicalCases_Appointments_AdmissionAppointmentId",
                table: "ClinicalCases",
                column: "AdmissionAppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Do not erase a visit's provenance or invent an emergency grant on rollback.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ClinicalCases" WHERE "AdmissionAppointmentId" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back VisitCaseAdmission while visit-linked cases exist. Preserve records and roll forward.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_ClinicalCases_Appointments_AdmissionAppointmentId",
                table: "ClinicalCases");

            migrationBuilder.DropIndex(
                name: "IX_ClinicalCases_AdmissionAppointmentId",
                table: "ClinicalCases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClinicalCases_AdmissionSource",
                table: "ClinicalCases");

            migrationBuilder.DropColumn(
                name: "AdmissionAppointmentId",
                table: "ClinicalCases");

            migrationBuilder.AlterColumn<Guid>(
                name: "AdmissionGrantId",
                table: "ClinicalCases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
