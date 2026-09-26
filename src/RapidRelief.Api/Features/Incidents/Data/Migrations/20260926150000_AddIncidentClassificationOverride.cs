using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Incidents.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentClassificationOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsClassificationOverridden",
                table: "incidents_reports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverriddenAtUtc",
                table: "incidents_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OverriddenByGovernmentId",
                table: "incidents_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideReason",
                table: "incidents_reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsClassificationOverridden",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "OverriddenAtUtc",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "OverriddenByGovernmentId",
                table: "incidents_reports");

            migrationBuilder.DropColumn(
                name: "OverrideReason",
                table: "incidents_reports");
        }
    }
}
