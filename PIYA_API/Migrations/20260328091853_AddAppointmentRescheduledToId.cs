using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PIYA_API.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentRescheduledToId : Migration
    {
        // MigrateToUtcTimestamps has no EF model delta, so its frozen target
        // model is exactly this generated migration's target model.
        internal static void BuildUtcMigrationTargetModel(ModelBuilder modelBuilder)
            => new AddAppointmentRescheduledToId().BuildTargetModel(modelBuilder);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RescheduledToId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RescheduledToId",
                table: "Appointments",
                column: "RescheduledToId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Appointments_RescheduledToId",
                table: "Appointments",
                column: "RescheduledToId",
                principalTable: "Appointments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Appointments_RescheduledToId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_RescheduledToId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "RescheduledToId",
                table: "Appointments");
        }
    }
}
