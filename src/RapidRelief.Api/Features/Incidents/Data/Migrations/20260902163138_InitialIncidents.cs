using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Incidents.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "incidents_reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReporterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DisasterType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    AddressOrArea = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    AffectedPeopleCount = table.Column<int>(type: "integer", nullable: false),
                    IsSos = table.Column<bool>(type: "boolean", nullable: false),
                    AiSeverityScore = table.Column<int>(type: "integer", nullable: false),
                    AiSummary = table.Column<string>(type: "text", nullable: false),
                    VerifiedByGovernmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents_reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "incidents_media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Caption = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents_media", x => x.Id);
                    table.ForeignKey(
                        name: "FK_incidents_media_incidents_reports_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "incidents_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incidents_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_incidents_status_history_incidents_reports_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "incidents_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_media_IncidentId",
                table: "incidents_media",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reports_CreatedAtUtc",
                table: "incidents_reports",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reports_DisasterType",
                table: "incidents_reports",
                column: "DisasterType");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reports_ReporterId",
                table: "incidents_reports",
                column: "ReporterId");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_reports_Status",
                table: "incidents_reports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_status_history_IncidentId",
                table: "incidents_status_history",
                column: "IncidentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "incidents_media");

            migrationBuilder.DropTable(
                name: "incidents_status_history");

            migrationBuilder.DropTable(
                name: "incidents_reports");
        }
    }
}
