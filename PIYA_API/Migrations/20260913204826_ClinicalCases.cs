using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class ClinicalCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendingDoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionGrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Bed = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AdmissionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    AdmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalCases_EmergencyAccessGrants_AdmissionGrantId",
                        column: x => x.AdmissionGrantId,
                        principalTable: "EmergencyAccessGrants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalCases_Hospitals_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalCases_Users_AttendingDoctorId",
                        column: x => x.AttendingDoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalCases_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalCaseEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RelatedEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    HeartRate = table.Column<double>(type: "double precision", nullable: true),
                    OxygenSaturation = table.Column<double>(type: "double precision", nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalCaseEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalCaseEvents_ClinicalCases_ClinicalCaseId",
                        column: x => x.ClinicalCaseId,
                        principalTable: "ClinicalCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalCaseEvents_ClinicalCaseId_RecordedAt",
                table: "ClinicalCaseEvents",
                columns: new[] { "ClinicalCaseId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalCases_AdmissionGrantId",
                table: "ClinicalCases",
                column: "AdmissionGrantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalCases_AttendingDoctorId",
                table: "ClinicalCases",
                column: "AttendingDoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalCases_HospitalId",
                table: "ClinicalCases",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalCases_PatientId_HospitalId",
                table: "ClinicalCases",
                columns: new[] { "PatientId", "HospitalId" },
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicalCaseEvents");

            migrationBuilder.DropTable(
                name: "ClinicalCases");
        }
    }
}
