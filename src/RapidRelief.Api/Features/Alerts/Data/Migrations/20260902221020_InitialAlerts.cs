using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Alerts.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alerts_alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorGovernmentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DisasterType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    TargetArea = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RadiusKm = table.Column<double>(type: "double precision", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts_alerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_alerts_CreatedAtUtc",
                table: "alerts_alerts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_alerts_alerts_ExpiresAtUtc_RevokedAtUtc",
                table: "alerts_alerts",
                columns: new[] { "ExpiresAtUtc", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts_alerts");
        }
    }
}
