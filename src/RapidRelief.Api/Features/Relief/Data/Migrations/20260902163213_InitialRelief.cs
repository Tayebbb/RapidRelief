using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Relief.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialRelief : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relief_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReliefType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UrgencyLevel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    QuantityRequested = table.Column<int>(type: "integer", nullable: false),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    DeliveryAddress = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relief_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "relief_resources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TotalQuantity = table.Column<double>(type: "double precision", nullable: false),
                    AllocatedQuantity = table.Column<double>(type: "double precision", nullable: false),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    WarehouseLocation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relief_resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "relief_dispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReliefRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DispatchedQuantity = table.Column<double>(type: "double precision", nullable: false),
                    DispatchedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierOrPartner = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DispatchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relief_dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_relief_dispatches_relief_requests_ReliefRequestId",
                        column: x => x.ReliefRequestId,
                        principalTable: "relief_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_relief_dispatches_relief_resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "relief_resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_relief_dispatches_ReliefRequestId",
                table: "relief_dispatches",
                column: "ReliefRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_relief_dispatches_ResourceId",
                table: "relief_dispatches",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_relief_requests_RequesterId",
                table: "relief_requests",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_relief_requests_Status",
                table: "relief_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_relief_resources_Category",
                table: "relief_resources",
                column: "Category");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relief_dispatches");

            migrationBuilder.DropTable(
                name: "relief_requests");

            migrationBuilder.DropTable(
                name: "relief_resources");
        }
    }
}
