using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class ReferralChain_EmergencyTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MedicalTests_Referrals_ReferralId",
                table: "MedicalTests");

            migrationBuilder.DropForeignKey(
                name: "FK_Tokens_Users_UserId",
                table: "Tokens");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DateOfBirth",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceAppointmentId",
                table: "Referrals",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "Referrals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentReferralId",
                table: "Referrals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ReferralId",
                table: "MedicalTests",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "IsEmergency",
                table: "MedicalTests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PatientId",
                table: "MedicalTests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Referrals_ParentReferralId",
                table: "Referrals",
                column: "ParentReferralId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalTests_PatientId",
                table: "MedicalTests",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_MedicalTests_Referrals_ReferralId",
                table: "MedicalTests",
                column: "ReferralId",
                principalTable: "Referrals",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MedicalTests_Users_PatientId",
                table: "MedicalTests",
                column: "PatientId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Referrals_Referrals_ParentReferralId",
                table: "Referrals",
                column: "ParentReferralId",
                principalTable: "Referrals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tokens_Users_UserId",
                table: "Tokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MedicalTests_Referrals_ReferralId",
                table: "MedicalTests");

            migrationBuilder.DropForeignKey(
                name: "FK_MedicalTests_Users_PatientId",
                table: "MedicalTests");

            migrationBuilder.DropForeignKey(
                name: "FK_Referrals_Referrals_ParentReferralId",
                table: "Referrals");

            migrationBuilder.DropForeignKey(
                name: "FK_Tokens_Users_UserId",
                table: "Tokens");

            migrationBuilder.DropIndex(
                name: "IX_Referrals_ParentReferralId",
                table: "Referrals");

            migrationBuilder.DropIndex(
                name: "IX_MedicalTests_PatientId",
                table: "MedicalTests");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "ParentReferralId",
                table: "Referrals");

            migrationBuilder.DropColumn(
                name: "IsEmergency",
                table: "MedicalTests");

            migrationBuilder.DropColumn(
                name: "PatientId",
                table: "MedicalTests");

            migrationBuilder.AlterColumn<DateTime>(
                name: "DateOfBirth",
                table: "Users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "Tokens",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceAppointmentId",
                table: "Referrals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ReferralId",
                table: "MedicalTests",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MedicalTests_Referrals_ReferralId",
                table: "MedicalTests",
                column: "ReferralId",
                principalTable: "Referrals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Tokens_Users_UserId",
                table: "Tokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
