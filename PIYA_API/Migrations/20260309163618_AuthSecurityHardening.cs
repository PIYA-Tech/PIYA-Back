using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class AuthSecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Family",
                table: "Tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "UsedRefreshTokens",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    Family = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsedRefreshTokens", x => x.TokenHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_Family",
                table: "Tokens",
                column: "Family");

            migrationBuilder.CreateIndex(
                name: "IX_UsedRefreshTokens_ExpiresAt",
                table: "UsedRefreshTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UsedRefreshTokens_Family",
                table: "UsedRefreshTokens",
                column: "Family");

            migrationBuilder.CreateIndex(
                name: "IX_UsedRefreshTokens_UserId",
                table: "UsedRefreshTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsedRefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Tokens_Family",
                table: "Tokens");

            migrationBuilder.DropColumn(
                name: "Family",
                table: "Tokens");
        }
    }
}
