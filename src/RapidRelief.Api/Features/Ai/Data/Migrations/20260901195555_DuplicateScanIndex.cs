using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Ai.Data.Migrations
{
    /// <inheritdoc />
    public partial class DuplicateScanIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ai_assessments_SnapshotType_SnapshotReportedAtUtc",
                table: "ai_assessments",
                columns: new[] { "SnapshotType", "SnapshotReportedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ai_assessments_SnapshotType_SnapshotReportedAtUtc",
                table: "ai_assessments");
        }
    }
}
