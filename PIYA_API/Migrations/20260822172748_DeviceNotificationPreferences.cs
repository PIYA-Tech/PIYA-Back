using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class DeviceNotificationPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AppointmentNotificationsEnabled",
                table: "DeviceTokens",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "MedicationReminderNotificationsEnabled",
                table: "DeviceTokens",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NewsNotificationsEnabled",
                table: "DeviceTokens",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PrescriptionNotificationsEnabled",
                table: "DeviceTokens",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppointmentNotificationsEnabled",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "MedicationReminderNotificationsEnabled",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "NewsNotificationsEnabled",
                table: "DeviceTokens");

            migrationBuilder.DropColumn(
                name: "PrescriptionNotificationsEnabled",
                table: "DeviceTokens");
        }
    }
}
