using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenUserIdAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Tokens_TokensInfoId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TokensInfoId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Password",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TokensInfoId",
                table: "Users");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PharmacistProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseNumber = table.Column<string>(type: "text", nullable: false),
                    LicenseAuthority = table.Column<string>(type: "text", nullable: true),
                    LicenseIssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LicenseExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LicenseStatus = table.Column<int>(type: "integer", nullable: false),
                    LastVerificationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextVerificationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationNotes = table.Column<string>(type: "text", nullable: true),
                    YearsOfExperience = table.Column<int>(type: "integer", nullable: false),
                    Education = table.Column<List<string>>(type: "text[]", nullable: false),
                    Certifications = table.Column<List<string>>(type: "text[]", nullable: false),
                    Languages = table.Column<List<string>>(type: "text[]", nullable: false),
                    Specializations = table.Column<List<string>>(type: "text[]", nullable: false),
                    Biography = table.Column<string>(type: "text", nullable: true),
                    PrimaryPharmacyId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptingConsultations = table.Column<bool>(type: "boolean", nullable: false),
                    TotalConsultations = table.Column<int>(type: "integer", nullable: false),
                    AverageRating = table.Column<decimal>(type: "numeric", nullable: true),
                    TotalRatings = table.Column<int>(type: "integer", nullable: false),
                    EnableExpiryReminders = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderDaysBeforeExpiry = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PharmacistProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PharmacistProfiles_Pharmacies_PrimaryPharmacyId",
                        column: x => x.PrimaryPharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PharmacistProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_RefreshToken",
                table: "Tokens",
                column: "RefreshToken");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_UserId",
                table: "Tokens",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PharmacistProfiles_PrimaryPharmacyId",
                table: "PharmacistProfiles",
                column: "PrimaryPharmacyId");

            migrationBuilder.CreateIndex(
                name: "IX_PharmacistProfiles_UserId",
                table: "PharmacistProfiles",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tokens_Users_UserId",
                table: "Tokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tokens_Users_UserId",
                table: "Tokens");

            migrationBuilder.DropTable(
                name: "PharmacistProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Tokens_RefreshToken",
                table: "Tokens");

            migrationBuilder.DropIndex(
                name: "IX_Tokens_UserId",
                table: "Tokens");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Tokens");

            migrationBuilder.AddColumn<string>(
                name: "Password",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TokensInfoId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Users_TokensInfoId",
                table: "Users",
                column: "TokensInfoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Tokens_TokensInfoId",
                table: "Users",
                column: "TokensInfoId",
                principalTable: "Tokens",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
