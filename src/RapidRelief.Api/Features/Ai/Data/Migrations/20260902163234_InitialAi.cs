using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Ai.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAi : Migration
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

            migrationBuilder.CreateTable(
                name: "ai_assistant_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_assistant_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_assessments_IncidentId",
                table: "ai_assessments",
                column: "IncidentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_assessments_SnapshotType_SnapshotReportedAtUtc",
                table: "ai_assessments",
                columns: new[] { "SnapshotType", "SnapshotReportedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_assistant_messages_SessionId_CreatedAtUtc",
                table: "ai_assistant_messages",
                columns: new[] { "SessionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_assistant_messages_UserId_CreatedAtUtc",
                table: "ai_assistant_messages",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_assistant_messages_UserId_SessionId_CreatedAtUtc",
                table: "ai_assistant_messages",
                columns: new[] { "UserId", "SessionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_TimestampUtc",
                table: "audit_logs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_UserId",
                table: "audit_logs",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_assessments");

            migrationBuilder.DropTable(
                name: "ai_assistant_messages");

            migrationBuilder.DropTable(
                name: "audit_logs");
        }
    }
}
