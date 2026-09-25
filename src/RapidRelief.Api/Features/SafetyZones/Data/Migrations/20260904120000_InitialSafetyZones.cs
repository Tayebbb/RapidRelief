using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.SafetyZones.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSafetyZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "safety_zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ZoneType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    GeometryType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CenterLat = table.Column<double>(type: "double precision", nullable: false),
                    CenterLng = table.Column<double>(type: "double precision", nullable: false),
                    RadiusMeters = table.Column<double>(type: "double precision", nullable: false),
                    CoordinatesJson = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_safety_zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "safety_road_closures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CoordinatesJson = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    BlockedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AlternateRouteAdvice = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ReportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EstimatedReopenUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_safety_road_closures", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_safety_zones_CreatedAtUtc",
                table: "safety_zones",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_safety_zones_IsActive",
                table: "safety_zones",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_safety_zones_ZoneType",
                table: "safety_zones",
                column: "ZoneType");

            migrationBuilder.CreateIndex(
                name: "IX_safety_road_closures_IsActive",
                table: "safety_road_closures",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_safety_road_closures_ReportedAtUtc",
                table: "safety_road_closures",
                column: "ReportedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_safety_road_closures_Severity",
                table: "safety_road_closures",
                column: "Severity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "safety_road_closures");

            migrationBuilder.DropTable(
                name: "safety_zones");
        }
    }
}
