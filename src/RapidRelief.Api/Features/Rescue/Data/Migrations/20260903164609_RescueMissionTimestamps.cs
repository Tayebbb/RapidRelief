using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Rescue.Data.Migrations
{
    /// <inheritdoc />
    public partial class RescueMissionTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcceptedAtUtc",
                table: "rescue_missions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OnSceneAtUtc",
                table: "rescue_missions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "rescue_missions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedAtUtc",
                table: "rescue_missions");

            migrationBuilder.DropColumn(
                name: "OnSceneAtUtc",
                table: "rescue_missions");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "rescue_missions");
        }
    }
}
