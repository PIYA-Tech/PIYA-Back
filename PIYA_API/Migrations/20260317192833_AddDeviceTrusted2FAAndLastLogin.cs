using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceTrusted2FAAndLastLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceName",
                table: "DeviceTokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrusted2FADevice",
                table: "DeviceTokens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "DeviceTokens",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "IsTrusted2FADevice",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "DeviceTokens");
        }
    }
}
