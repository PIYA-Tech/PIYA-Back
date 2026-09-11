using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSessionSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SecurityStamp",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "SecurityStamp",
                table: "Tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Existing sessions have no credential-version binding. Give every
            // account a distinct stamp; old tokens deliberately remain unbound
            // and are rejected. All users must sign in again after this upgrade.
            migrationBuilder.Sql("UPDATE \"Users\" SET \"SecurityStamp\" = gen_random_uuid();");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_UserId_Family",
                table: "Tokens",
                columns: new[] { "UserId", "Family" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tokens_UserId_Family",
                table: "Tokens");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Tokens");
        }
    }
}
