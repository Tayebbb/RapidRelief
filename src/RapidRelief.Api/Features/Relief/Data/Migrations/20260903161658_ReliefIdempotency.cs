using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Relief.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReliefIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "relief_requests",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_relief_requests_RequesterId_IdempotencyKey",
                table: "relief_requests",
                columns: new[] { "RequesterId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_relief_requests_RequesterId_IdempotencyKey",
                table: "relief_requests");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "relief_requests");
        }
    }
}
