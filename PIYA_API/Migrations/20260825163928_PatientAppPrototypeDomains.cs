using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class PatientAppPrototypeDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CareCircleInvitation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteeEmailNormalized = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    RequestedScopes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccessExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeclinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RespondedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareCircleInvitation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareCircleInvitation_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConsultationSummary",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Diagnosis = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CareInstructions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    WarningSigns = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PatientMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LastAmendmentReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultationSummary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsultationSummary_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsultationSummary_Users_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsultationSummary_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedicalTestAnalyteResult",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalTestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NumericValue = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    TextValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Unit = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReferenceLow = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ReferenceHigh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ReferenceText = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Flag = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EnteredByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalTestAnalyteResult", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicalTestAnalyteResult_MedicalTests_MedicalTestId",
                        column: x => x.MedicalTestId,
                        principalTable: "MedicalTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MedicalTestAnalyteResult_Users_EnteredByUserId",
                        column: x => x.EnteredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientInboxNotification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ActionRoute = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DataJson = table.Column<string>(type: "text", nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientInboxNotification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientInboxNotification_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatientMedication",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PrescriptionItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GenericName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Strength = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Form = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Dosage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupplyTotal = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    SupplyRemaining = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    SupplyUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LowSupplyThreshold = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    LastRefilledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientMedication", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientMedication_Medications_MedicationId",
                        column: x => x.MedicationId,
                        principalTable: "Medications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PatientMedication_PrescriptionItems_PrescriptionItemId",
                        column: x => x.PrescriptionItemId,
                        principalTable: "PrescriptionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PatientMedication_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatientRefillStatusEvent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefillRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EstimatedReadyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientRefillStatusEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientRefillStatusEvent_PatientRefillRequests_RefillReques~",
                        column: x => x.RefillRequestId,
                        principalTable: "PatientRefillRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PatientRefillStatusEvent_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientVerification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ProviderReference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ActionUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    DocumentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    InsuranceIssuer = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PolicyReferenceLastFour = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    StatusReasonCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientVerification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientVerification_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PharmacyPickup",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RefillRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PharmacyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityCollected = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PharmacyPickup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PharmacyPickup_PatientRefillRequests_RefillRequestId",
                        column: x => x.RefillRequestId,
                        principalTable: "PatientRefillRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PharmacyPickup_Pharmacies_PharmacyId",
                        column: x => x.PharmacyId,
                        principalTable: "Pharmacies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PharmacyPickup_PrescriptionItems_PrescriptionItemId",
                        column: x => x.PrescriptionItemId,
                        principalTable: "PrescriptionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PharmacyPickup_Users_CollectedByUserId",
                        column: x => x.CollectedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PharmacyPickup_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareCircleMember",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeftAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareCircleMember", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareCircleMember_CareCircleInvitation_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "CareCircleInvitation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareCircleMember_Users_MemberUserId",
                        column: x => x.MemberUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareCircleMember_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareLoopWorkflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConsultationSummaryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PausedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareLoopWorkflow", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareLoopWorkflow_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareLoopWorkflow_ConsultationSummary_ConsultationSummaryId",
                        column: x => x.ConsultationSummaryId,
                        principalTable: "ConsultationSummary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareLoopWorkflow_Users_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareLoopWorkflow_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FollowUpPlan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultationSummaryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    EarliestAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LatestAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ScheduledAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeclinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeclineReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpPlan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpPlan_Appointments_ScheduledAppointmentId",
                        column: x => x.ScheduledAppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpPlan_Appointments_SourceAppointmentId",
                        column: x => x.SourceAppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpPlan_ConsultationSummary_ConsultationSummaryId",
                        column: x => x.ConsultationSummaryId,
                        principalTable: "ConsultationSummary",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpPlan_Hospitals_HospitalId",
                        column: x => x.HospitalId,
                        principalTable: "Hospitals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpPlan_Users_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpPlan_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedicationDoseSchedule",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientMedicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoseAmount = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    TimeOfDayMinutes = table.Column<int>(type: "integer", nullable: false),
                    DaysOfWeek = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RemindersEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    GracePeriodMinutes = table.Column<int>(type: "integer", nullable: false),
                    NextReminderAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationDoseSchedule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicationDoseSchedule_PatientMedication_PatientMedicationId",
                        column: x => x.PatientMedicationId,
                        principalTable: "PatientMedication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CareCircleConsent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scopes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareCircleConsent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareCircleConsent_CareCircleMember_MemberId",
                        column: x => x.MemberId,
                        principalTable: "CareCircleMember",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CareCircleConsent_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CareLoopTask",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PatientResponse = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SkippedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SkipReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareLoopTask", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareLoopTask_CareLoopWorkflow_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "CareLoopWorkflow",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicationDoseOccurrence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientMedicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledFor = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReminderSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupplyDeducted = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    ClientEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationDoseOccurrence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicationDoseOccurrence_MedicationDoseSchedule_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "MedicationDoseSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MedicationDoseOccurrence_PatientMedication_PatientMedicatio~",
                        column: x => x.PatientMedicationId,
                        principalTable: "PatientMedication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MedicationDoseOccurrence_Users_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareCircleConsent_MemberId",
                table: "CareCircleConsent",
                column: "MemberId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CareCircleConsent_PatientId_Status_ExpiresAt",
                table: "CareCircleConsent",
                columns: new[] { "PatientId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CareCircleInvitation_PatientId_InviteeEmailNormalized_Status",
                table: "CareCircleInvitation",
                columns: new[] { "PatientId", "InviteeEmailNormalized", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CareCircleInvitation_TokenHash",
                table: "CareCircleInvitation",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareCircleMember_InvitationId",
                table: "CareCircleMember",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_CareCircleMember_MemberUserId_Status_ExpiresAt",
                table: "CareCircleMember",
                columns: new[] { "MemberUserId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CareCircleMember_PatientId_MemberUserId",
                table: "CareCircleMember",
                columns: new[] { "PatientId", "MemberUserId" },
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CareLoopTask_Status_DueAt",
                table: "CareLoopTask",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CareLoopTask_WorkflowId_DueAt",
                table: "CareLoopTask",
                columns: new[] { "WorkflowId", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CareLoopWorkflow_AppointmentId",
                table: "CareLoopWorkflow",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CareLoopWorkflow_ConsultationSummaryId",
                table: "CareLoopWorkflow",
                column: "ConsultationSummaryId");

            migrationBuilder.CreateIndex(
                name: "IX_CareLoopWorkflow_DoctorId",
                table: "CareLoopWorkflow",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_CareLoopWorkflow_PatientId_Status_UpdatedAt",
                table: "CareLoopWorkflow",
                columns: new[] { "PatientId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationSummary_AppointmentId",
                table: "ConsultationSummary",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationSummary_DoctorId",
                table: "ConsultationSummary",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultationSummary_PatientId_Status_PublishedAt",
                table: "ConsultationSummary",
                columns: new[] { "PatientId", "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPlan_ConsultationSummaryId_Status",
                table: "FollowUpPlan",
                columns: new[] { "ConsultationSummaryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPlan_DoctorId",
                table: "FollowUpPlan",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPlan_HospitalId",
                table: "FollowUpPlan",
                column: "HospitalId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPlan_PatientId_Status_EarliestAt",
                table: "FollowUpPlan",
                columns: new[] { "PatientId", "Status", "EarliestAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPlan_ScheduledAppointmentId",
                table: "FollowUpPlan",
                column: "ScheduledAppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpPlan_SourceAppointmentId",
                table: "FollowUpPlan",
                column: "SourceAppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalTestAnalyteResult_EnteredByUserId",
                table: "MedicalTestAnalyteResult",
                column: "EnteredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalTestAnalyteResult_MedicalTestId_Code",
                table: "MedicalTestAnalyteResult",
                columns: new[] { "MedicalTestId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDoseOccurrence_PatientId_ClientEventId",
                table: "MedicationDoseOccurrence",
                columns: new[] { "PatientId", "ClientEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDoseOccurrence_PatientId_ScheduledFor",
                table: "MedicationDoseOccurrence",
                columns: new[] { "PatientId", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDoseOccurrence_PatientMedicationId",
                table: "MedicationDoseOccurrence",
                column: "PatientMedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDoseOccurrence_ScheduleId_ScheduledFor",
                table: "MedicationDoseOccurrence",
                columns: new[] { "ScheduleId", "ScheduledFor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDoseSchedule_PatientMedicationId",
                table: "MedicationDoseSchedule",
                column: "PatientMedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDoseSchedule_RemindersEnabled_NextReminderAt",
                table: "MedicationDoseSchedule",
                columns: new[] { "RemindersEnabled", "NextReminderAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientInboxNotification_UserId_CreatedAt",
                table: "PatientInboxNotification",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientInboxNotification_UserId_DedupeKey",
                table: "PatientInboxNotification",
                columns: new[] { "UserId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientInboxNotification_UserId_ReadAt",
                table: "PatientInboxNotification",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientMedication_MedicationId",
                table: "PatientMedication",
                column: "MedicationId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientMedication_PatientId_PrescriptionItemId",
                table: "PatientMedication",
                columns: new[] { "PatientId", "PrescriptionItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientMedication_PatientId_Status",
                table: "PatientMedication",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientMedication_PrescriptionItemId",
                table: "PatientMedication",
                column: "PrescriptionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientRefillStatusEvent_ActorUserId",
                table: "PatientRefillStatusEvent",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientRefillStatusEvent_RefillRequestId_OccurredAt",
                table: "PatientRefillStatusEvent",
                columns: new[] { "RefillRequestId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientVerification_PatientId_Kind_CreatedAt",
                table: "PatientVerification",
                columns: new[] { "PatientId", "Kind", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientVerification_ProviderName_ProviderReference",
                table: "PatientVerification",
                columns: new[] { "ProviderName", "ProviderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyPickup_CollectedByUserId",
                table: "PharmacyPickup",
                column: "CollectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyPickup_PatientId_CollectedAt",
                table: "PharmacyPickup",
                columns: new[] { "PatientId", "CollectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyPickup_PharmacyId",
                table: "PharmacyPickup",
                column: "PharmacyId");

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyPickup_PrescriptionItemId",
                table: "PharmacyPickup",
                column: "PrescriptionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyPickup_RefillRequestId",
                table: "PharmacyPickup",
                column: "RefillRequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareCircleConsent");

            migrationBuilder.DropTable(
                name: "CareLoopTask");

            migrationBuilder.DropTable(
                name: "FollowUpPlan");

            migrationBuilder.DropTable(
                name: "MedicalTestAnalyteResult");

            migrationBuilder.DropTable(
                name: "MedicationDoseOccurrence");

            migrationBuilder.DropTable(
                name: "PatientInboxNotification");

            migrationBuilder.DropTable(
                name: "PatientRefillStatusEvent");

            migrationBuilder.DropTable(
                name: "PatientVerification");

            migrationBuilder.DropTable(
                name: "PharmacyPickup");

            migrationBuilder.DropTable(
                name: "CareCircleMember");

            migrationBuilder.DropTable(
                name: "CareLoopWorkflow");

            migrationBuilder.DropTable(
                name: "MedicationDoseSchedule");

            migrationBuilder.DropTable(
                name: "CareCircleInvitation");

            migrationBuilder.DropTable(
                name: "ConsultationSummary");

            migrationBuilder.DropTable(
                name: "PatientMedication");
        }
    }
}
