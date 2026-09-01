using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Ai.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PredictedType = table.Column<int>(type: "integer", nullable: false),
                    EstimatedSeverity = table.Column<int>(type: "integer", nullable: false),
                    PriorityScore = table.Column<double>(type: "double precision", nullable: false),
                    Summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PossibleDuplicateOfId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    TokensUsed = table.Column<int>(type: "integer", nullable: true),
                    FinishReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SnapshotLatitude = table.Column<double>(type: "double precision", nullable: false),
                    SnapshotLongitude = table.Column<double>(type: "double precision", nullable: false),
                    SnapshotType = table.Column<int>(type: "integer", nullable: false),
                    SnapshotReportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SnapshotIsSos = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_assessments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_assessments_IncidentId",
                table: "ai_assessments",
                column: "IncidentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_assessments");
        }
    }
}
