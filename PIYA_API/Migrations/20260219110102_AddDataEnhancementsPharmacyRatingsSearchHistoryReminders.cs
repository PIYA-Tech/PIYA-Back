using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class AddDataEnhancementsPharmacyRatingsSearchHistoryReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AverageRating",
                table: "Pharmacies",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Pharmacies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Pharmacies",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Pharmacies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContact",
                table: "Pharmacies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Is24Hours",
                table: "Pharmacies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Pharmacies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OperatingHours",
                table: "Pharmacies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Pharmacies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Services",
                table: "Pharmacies",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<int>(
                name: "TotalRatings",
                table: "Pharmacies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Pharmacies",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Pharmacies",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReminderTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MinutesBeforeAppointment = table.Column<int>(type: "integer", nullable: false),
                    DeliveryMethods = table.Column<int[]>(type: "integer[]", nullable: false),
                    IsSent = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveryStatus = table.Column<string>(type: "text", nullable: true),
                    CustomMessage = table.Column<string>(type: "text", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentReminders_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppointmentReminders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PharmacyRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    ReviewText = table.Column<string>(type: "text", nullable: true),
                    WouldRecommend = table.Column<bool>(type: "boolean", nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Categories = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PharmacyRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PharmacyRatings_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PharmacyRatings_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PharmacyRatings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrescriptionRefillReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReminderDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedRefillDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DaysBeforeRefill = table.Column<int>(type: "integer", nullable: false),
                    DeliveryMethods = table.Column<int[]>(type: "integer[]", nullable: false),
                    IsSent = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    IsRefilled = table.Column<bool>(type: "boolean", nullable: false),
                    RefillPrescriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    MedicationItemIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionRefillReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrescriptionRefillReminders_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrescriptionRefillReminders_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SearchType = table.Column<int>(type: "integer", nullable: false),
                    SearchQuery = table.Column<string>(type: "text", nullable: true),
                    Filters = table.Column<string>(type: "text", nullable: true),
                    ResultCount = table.Column<int>(type: "integer", nullable: false),
                    CoordinatesId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedResultId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectedResultType = table.Column<string>(type: "text", nullable: true),
                    SearchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchHistories_Coordinates_CoordinatesId",
                        column: x => x.CoordinatesId,
                        principalTable: "Coordinates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SearchHistories_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminders_AppointmentId",
                table: "AppointmentReminders",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminders_IsSent",
                table: "AppointmentReminders",
                column: "IsSent");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminders_ReminderTime",
                table: "AppointmentReminders",
                column: "ReminderTime");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminders_UserId",
                table: "AppointmentReminders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyRatings_PharmacyId",
                table: "PharmacyRatings",
                column: "PharmacyId");

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyRatings_PharmacyId_UserId",
                table: "PharmacyRatings",
                columns: new[] { "PharmacyId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyRatings_PrescriptionId",
                table: "PharmacyRatings",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyRatings_UserId",
                table: "PharmacyRatings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionRefillReminders_IsSent",
                table: "PrescriptionRefillReminders",
                column: "IsSent");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionRefillReminders_PatientId",
                table: "PrescriptionRefillReminders",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionRefillReminders_PrescriptionId",
                table: "PrescriptionRefillReminders",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionRefillReminders_ReminderDate",
                table: "PrescriptionRefillReminders",
                column: "ReminderDate");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_CoordinatesId",
                table: "SearchHistories",
                column: "CoordinatesId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_SearchedAt",
                table: "SearchHistories",
                column: "SearchedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_SearchType",
                table: "SearchHistories",
                column: "SearchType");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistories_UserId",
                table: "SearchHistories",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentReminders");

            migrationBuilder.DropTable(
                name: "PharmacyRatings");

            migrationBuilder.DropTable(
                name: "PrescriptionRefillReminders");

            migrationBuilder.DropTable(
                name: "SearchHistories");

            migrationBuilder.DropColumn(
                name: "AverageRating",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "EmergencyContact",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "Is24Hours",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "OperatingHours",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "Services",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "TotalRatings",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Pharmacies");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Pharmacies");
        }
    }
}
