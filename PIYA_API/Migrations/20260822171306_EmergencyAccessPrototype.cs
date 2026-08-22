using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class EmergencyAccessPrototype : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmergencyAccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FacilityName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyAccessGrants_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmergencyAccessGrants_Users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmergencyHealthProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    BloodType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Allergies = table.Column<string>(type: "text", nullable: true),
                    ChronicConditions = table.Column<string>(type: "text", nullable: true),
                    CurrentMedications = table.Column<string>(type: "text", nullable: true),
                    EmergencyContacts = table.Column<string>(type: "text", nullable: true),
                    AdditionalNotes = table.Column<string>(type: "text", nullable: true),
                    IsSharingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ShareTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ShareTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyHealthProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencyHealthProfiles_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyAccessGrants_ExpiresAt",
                table: "EmergencyAccessGrants",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyAccessGrants_PatientId",
                table: "EmergencyAccessGrants",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyAccessGrants_RequesterId",
                table: "EmergencyAccessGrants",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyHealthProfiles_PatientId",
                table: "EmergencyHealthProfiles",
                column: "PatientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyHealthProfiles_ShareTokenHash",
                table: "EmergencyHealthProfiles",
                column: "ShareTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmergencyAccessGrants");

            migrationBuilder.DropTable(
                name: "EmergencyHealthProfiles");
        }
    }
}
