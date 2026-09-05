using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RapidRelief.Api.Features.Ai.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiDecisionSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "ai_assessments",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "DamageIndicatorsJson",
                table: "ai_assessments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DegradedReason",
                table: "ai_assessments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DuplicateConfidence",
                table: "ai_assessments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateDecision",
                table: "ai_assessments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DuplicateReason",
                table: "ai_assessments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DuplicateReviewedAtUtc",
                table: "ai_assessments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DuplicateReviewedByUserId",
                table: "ai_assessments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedPeopleAffected",
                table: "ai_assessments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MedicalUrgency",
                table: "ai_assessments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PriorityBand",
                table: "ai_assessments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PriorityFactorsJson",
                table: "ai_assessments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Reasoning",
                table: "ai_assessments",
                type: "character varying(600)",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SnapshotDescriptionKey",
                table: "ai_assessments",
                type: "character varying(600)",
                maxLength: 600,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Urgency",
                table: "ai_assessments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ai_assessments_PossibleDuplicateOfId_DuplicateDecision",
                table: "ai_assessments",
                columns: new[] { "PossibleDuplicateOfId", "DuplicateDecision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ai_assessments_PossibleDuplicateOfId_DuplicateDecision",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "DamageIndicatorsJson",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "DegradedReason",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "DuplicateConfidence",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "DuplicateDecision",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "DuplicateReason",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "DuplicateReviewedAtUtc",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "DuplicateReviewedByUserId",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "EstimatedPeopleAffected",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "MedicalUrgency",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "PriorityBand",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "PriorityFactorsJson",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "Reasoning",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "SnapshotDescriptionKey",
                table: "ai_assessments");

            migrationBuilder.DropColumn(
                name: "Urgency",
                table: "ai_assessments");
        }
    }
}
