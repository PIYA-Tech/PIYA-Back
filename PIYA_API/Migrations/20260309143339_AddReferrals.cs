using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class AddReferrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MedicalTestId",
                table: "MedicalDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferralId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Referrals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferringDoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferredToSpecialty = table.Column<int>(type: "integer", nullable: false),
                    ReferredToDoctorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Urgency = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ClinicalNotes = table.Column<string>(type: "text", nullable: true),
                    ResultNotes = table.Column<string>(type: "text", nullable: true),
                    IsExternal = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalProviderName = table.Column<string>(type: "text", nullable: true),
                    ExternalProviderContact = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Referrals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Referrals_Appointments_ResultAppointmentId",
                        column: x => x.ResultAppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Referrals_Appointments_SourceAppointmentId",
                        column: x => x.SourceAppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Referrals_Users_ReferredToDoctorId",
                        column: x => x.ReferredToDoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Referrals_Users_ReferringDoctorId",
                        column: x => x.ReferringDoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedicalTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferralId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderedByDoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedByDoctorId = table.Column<Guid>(type: "uuid", nullable: true),
                    TestType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    Findings = table.Column<string>(type: "text", nullable: true),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalTests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalTests_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MedicalTests_Referrals_ReferralId",
                        column: x => x.ReferralId,
                        principalTable: "Referrals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MedicalTests_Users_OrderedByDoctorId",
                        column: x => x.OrderedByDoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MedicalTests_Users_PerformedByDoctorId",
                        column: x => x.PerformedByDoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalDocuments_MedicalTestId",
                table: "MedicalDocuments",
                column: "MedicalTestId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalTests_AppointmentId",
                table: "MedicalTests",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalTests_OrderedByDoctorId",
                table: "MedicalTests",
                column: "OrderedByDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalTests_PerformedByDoctorId",
                table: "MedicalTests",
                column: "PerformedByDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalTests_ReferralId",
                table: "MedicalTests",
                column: "ReferralId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalTests_Status",
                table: "MedicalTests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_CreatedAt",
                table: "Referrals",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_PatientId",
                table: "Referrals",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferredToDoctorId",
                table: "Referrals",
                column: "ReferredToDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ReferringDoctorId",
                table: "Referrals",
                column: "ReferringDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ResultAppointmentId",
                table: "Referrals",
                column: "ResultAppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_SourceAppointmentId",
                table: "Referrals",
                column: "SourceAppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_Status",
                table: "Referrals",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_MedicalDocuments_MedicalTests_MedicalTestId",
                table: "MedicalDocuments",
                column: "MedicalTestId",
                principalTable: "MedicalTests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MedicalDocuments_MedicalTests_MedicalTestId",
                table: "MedicalDocuments");

            migrationBuilder.DropTable(
                name: "MedicalTests");

            migrationBuilder.DropTable(
                name: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_MedicalDocuments_MedicalTestId",
                table: "MedicalDocuments");

            migrationBuilder.DropColumn(
                name: "MedicalTestId",
                table: "MedicalDocuments");

            migrationBuilder.DropColumn(
                name: "ReferralId",
                table: "Appointments");
        }
    }
}
