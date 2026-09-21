using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Incidents.Data.Migrations
{
    /// <inheritdoc />
    public partial class IncidentLifecycleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssignedAtUtc",
                table: "incidents_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedMissionId",
                table: "incidents_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedTeamId",
                table: "incidents_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "incidents_reports",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "incidents_reports",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PossibleDuplicateOfId",
                table: "incidents_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PriorityScore",
                table: "incidents_reports",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "incidents_reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAtUtc",
                table: "incidents_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reports_ReporterId_IdempotencyKey",
                table: "incidents_reports",
                columns: new[] { "ReporterId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reports_Status_IsSos_PriorityScore",
                table: "incidents_reports",
                columns: new[] { "Status", "IsSos", "PriorityScore" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_incidents_reports_ReporterId_IdempotencyKey",
                table: "incidents_reports");

            migrationBuilder.DropIndex(
                name: "IX_incidents_reports_Status_IsSos_PriorityScore",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "AssignedAtUtc",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "AssignedMissionId",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "AssignedTeamId",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "PossibleDuplicateOfId",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "PriorityScore",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "incidents_reports");
        }
    }
}
