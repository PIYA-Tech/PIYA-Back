using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class NewRoles_DirectorAndNetworkOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "PharmacyCompanies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DirectorId",
                table: "Hospitals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PharmacyCompanies_OwnerId",
                table: "PharmacyCompanies",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Hospitals_DirectorId",
                table: "Hospitals",
                column: "DirectorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Hospitals_Users_DirectorId",
                table: "Hospitals",
                column: "DirectorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PharmacyCompanies_Users_OwnerId",
                table: "PharmacyCompanies",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Hospitals_Users_DirectorId",
                table: "Hospitals");

            migrationBuilder.DropForeignKey(
                name: "FK_PharmacyCompanies_Users_OwnerId",
                table: "PharmacyCompanies");

            migrationBuilder.DropIndex(
                name: "IX_PharmacyCompanies_OwnerId",
                table: "PharmacyCompanies");

            migrationBuilder.DropIndex(
                name: "IX_Hospitals_DirectorId",
                table: "Hospitals");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "PharmacyCompanies");

            migrationBuilder.DropColumn(
                name: "DirectorId",
                table: "Hospitals");
        }
    }
}
