using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Ai.Data.Migrations
{
    /// <inheritdoc />
    public partial class AssistantOwnerSessionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ai_assistant_messages_UserId_SessionId_CreatedAtUtc",
                table: "ai_assistant_messages",
                columns: new[] { "UserId", "SessionId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ai_assistant_messages_UserId_SessionId_CreatedAtUtc",
                table: "ai_assistant_messages");
        }
    }
}
