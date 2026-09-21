using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Realtime.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications_broadcasts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorGovernmentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AlertBody = table.Column<string>(type: "text", nullable: false),
                    TargetArea = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RadiusKm = table.Column<double>(type: "double precision", nullable: true),
                    Severity = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications_broadcasts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notifications_notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Audience = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Topic = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    PayloadJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications_notification", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notifications_read",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications_read", x => new { x.NotificationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_notifications_read_notifications_notification_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notifications_notification",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_notification_Audience_Role_CreatedAtUtc",
                table: "notifications_notification",
                columns: new[] { "Audience", "Role", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_notification_CreatedAtUtc_Id",
                table: "notifications_notification",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_notification_UserId_CreatedAtUtc",
                table: "notifications_notification",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_read_UserId_NotificationId",
                table: "notifications_read",
                columns: new[] { "UserId", "NotificationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications_broadcasts");

            migrationBuilder.DropTable(
                name: "notifications_read");

            migrationBuilder.DropTable(
                name: "notifications_notification");
        }
    }
}
