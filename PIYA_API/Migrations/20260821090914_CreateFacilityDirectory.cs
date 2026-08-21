using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class CreateFacilityDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DirectoryFacilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NormalizedName = table.Column<string>(type: "text", nullable: false),
                    LegalName = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    NormalizedAddress = table.Column<string>(type: "text", nullable: true),
                    City = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: false),
                    District = table.Column<string>(type: "text", nullable: true),
                    PhoneNumbers = table.Column<List<string>>(type: "text[]", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Website = table.Column<string>(type: "text", nullable: true),
                    OperatingHours = table.Column<string>(type: "text", nullable: true),
                    Services = table.Column<List<string>>(type: "text[]", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    Ownership = table.Column<int>(type: "integer", nullable: false),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    LicenseNumber = table.Column<string>(type: "text", nullable: true),
                    TaxId = table.Column<string>(type: "text", nullable: true),
                    PrimarySourceName = table.Column<string>(type: "text", nullable: true),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ParentFacilityId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationalHospitalId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationalPharmacyId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryFacilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DirectoryFacilities_DirectoryFacilities_ParentFacilityId",
                        column: x => x.ParentFacilityId,
                        principalTable: "DirectoryFacilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DirectoryFacilities_Hospitals_OperationalHospitalId",
                        column: x => x.OperationalHospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DirectoryFacilities_Pharmacies_OperationalPharmacyId",
                        column: x => x.OperationalPharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FacilityImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    RecordsRead = table.Column<int>(type: "integer", nullable: false),
                    RecordsCreated = table.Column<int>(type: "integer", nullable: false),
                    RecordsUpdated = table.Column<int>(type: "integer", nullable: false),
                    RecordsMatched = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCandidates = table.Column<int>(type: "integer", nullable: false),
                    RecordsSkipped = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacilityClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationName = table.Column<string>(type: "text", nullable: false),
                    LicenseNumber = table.Column<string>(type: "text", nullable: true),
                    TaxId = table.Column<string>(type: "text", nullable: true),
                    ContactEmail = table.Column<string>(type: "text", nullable: false),
                    ContactPhone = table.Column<string>(type: "text", nullable: true),
                    EvidenceNotes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewNotes = table.Column<string>(type: "text", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityClaims_DirectoryFacilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "DirectoryFacilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FacilityClaims_Users_ClaimedByUserId",
                        column: x => x.ClaimedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FacilityClaims_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "FacilityDuplicateCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    PossibleDuplicateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfidenceScore = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityDuplicateCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilityDuplicateCandidates_DirectoryFacilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "DirectoryFacilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FacilityDuplicateCandidates_DirectoryFacilities_PossibleDup~",
                        column: x => x.PossibleDuplicateId,
                        principalTable: "DirectoryFacilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FacilitySourceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    DataLicense = table.Column<string>(type: "text", nullable: true),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    RawName = table.Column<string>(type: "text", nullable: true),
                    RawAddress = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    SourceLastModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilitySourceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacilitySourceRecords_DirectoryFacilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "DirectoryFacilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryFacilities_City_Kind_IsPublished_IsActive",
                table: "DirectoryFacilities",
                columns: new[] { "City", "Kind", "IsPublished", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryFacilities_NormalizedAddress",
                table: "DirectoryFacilities",
                column: "NormalizedAddress");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryFacilities_NormalizedName",
                table: "DirectoryFacilities",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryFacilities_OperationalHospitalId",
                table: "DirectoryFacilities",
                column: "OperationalHospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryFacilities_OperationalPharmacyId",
                table: "DirectoryFacilities",
                column: "OperationalPharmacyId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryFacilities_ParentFacilityId",
                table: "DirectoryFacilities",
                column: "ParentFacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryFacilities_VerificationStatus",
                table: "DirectoryFacilities",
                column: "VerificationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FacilityClaims_ClaimedByUserId",
                table: "FacilityClaims",
                column: "ClaimedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FacilityClaims_FacilityId_Status",
                table: "FacilityClaims",
                columns: new[] { "FacilityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FacilityClaims_ReviewedByUserId",
                table: "FacilityClaims",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FacilityDuplicateCandidates_FacilityId_PossibleDuplicateId",
                table: "FacilityDuplicateCandidates",
                columns: new[] { "FacilityId", "PossibleDuplicateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacilityDuplicateCandidates_PossibleDuplicateId",
                table: "FacilityDuplicateCandidates",
                column: "PossibleDuplicateId");

            migrationBuilder.CreateIndex(
                name: "IX_FacilityDuplicateCandidates_Status",
                table: "FacilityDuplicateCandidates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FacilityImportRuns_SourceName_StartedAt",
                table: "FacilityImportRuns",
                columns: new[] { "SourceName", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FacilitySourceRecords_FacilityId",
                table: "FacilitySourceRecords",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_FacilitySourceRecords_SourceName_ExternalId",
                table: "FacilitySourceRecords",
                columns: new[] { "SourceName", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacilityClaims");

            migrationBuilder.DropTable(
                name: "FacilityDuplicateCandidates");

            migrationBuilder.DropTable(
                name: "FacilityImportRuns");

            migrationBuilder.DropTable(
                name: "FacilitySourceRecords");

            migrationBuilder.DropTable(
                name: "DirectoryFacilities");
        }
    }
}
