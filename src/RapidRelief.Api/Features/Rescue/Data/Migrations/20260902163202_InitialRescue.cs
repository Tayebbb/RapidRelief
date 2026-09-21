using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Rescue.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialRescue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rescue_teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    TeamLeadUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Specialization = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ContactNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CurrentLatitude = table.Column<double>(type: "double precision", nullable: true),
                    CurrentLongitude = table.Column<double>(type: "double precision", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rescue_teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rescue_missions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    MissionTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OutcomeNotes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rescue_missions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rescue_missions_rescue_teams_AssignedTeamId",
                        column: x => x.AssignedTeamId,
                        principalTable: "rescue_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rescue_team_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    RescuerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rescue_team_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rescue_team_members_rescue_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "rescue_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rescue_mission_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoggedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatusUpdate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rescue_mission_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rescue_mission_logs_rescue_missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "rescue_missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rescue_mission_logs_MissionId",
                table: "rescue_mission_logs",
                column: "MissionId");

            migrationBuilder.CreateIndex(
                name: "IX_rescue_missions_AssignedTeamId",
                table: "rescue_missions",
                column: "AssignedTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_rescue_missions_IncidentId",
                table: "rescue_missions",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_rescue_missions_Status",
                table: "rescue_missions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_rescue_team_members_RescuerUserId",
                table: "rescue_team_members",
                column: "RescuerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_rescue_team_members_TeamId",
                table: "rescue_team_members",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_rescue_teams_TeamLeadUserId",
                table: "rescue_teams",
                column: "TeamLeadUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rescue_mission_logs");

            migrationBuilder.DropTable(
                name: "rescue_team_members");

            migrationBuilder.DropTable(
                name: "rescue_missions");

            migrationBuilder.DropTable(
                name: "rescue_teams");
        }
    }
}
