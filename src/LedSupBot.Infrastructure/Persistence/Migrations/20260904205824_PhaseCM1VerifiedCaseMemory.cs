using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhaseCM1VerifiedCaseMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CaseMemoryAttempted",
                table: "ai_training_examples",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CaseMemoryCount",
                table: "ai_training_examples",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CaseMemoryReferences",
                table: "ai_training_examples",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "CaseMemoryUsed",
                table: "ai_training_examples",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CaseMemoryAttempted",
                table: "ai_reply_context_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CaseMemoryCount",
                table: "ai_reply_context_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CaseMemoryReferences",
                table: "ai_reply_context_snapshots",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "CaseMemoryUsed",
                table: "ai_reply_context_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "verified_support_case_memories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicalSupportCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PrimarySymptom = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    NormalizedSymptom = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DeviceManufacturer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DeviceFamily = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProblemCategory = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DiagnosticSummary = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    CompletedChecks = table.Column<string>(type: "jsonb", nullable: false),
                    FailedChecks = table.Column<string>(type: "jsonb", nullable: false),
                    SuccessfulChecks = table.Column<string>(type: "jsonb", nullable: false),
                    FinalResolution = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    ResolutionSummary = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    RelevantWorkflowFacts = table.Column<string>(type: "jsonb", nullable: false),
                    RelevantDiagnosticStateSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    SourceMessageIds = table.Column<string>(type: "jsonb", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    MemorySchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    RetrievalText = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(768)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_verified_support_case_memories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_verified_support_case_memories_Status_DeviceModel",
                table: "verified_support_case_memories",
                columns: new[] { "Status", "DeviceModel" });

            migrationBuilder.CreateIndex(
                name: "IX_verified_support_case_memories_TechnicalSupportCaseId",
                table: "verified_support_case_memories",
                column: "TechnicalSupportCaseId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "verified_support_case_memories");

            migrationBuilder.DropColumn(
                name: "CaseMemoryAttempted",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "CaseMemoryCount",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "CaseMemoryReferences",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "CaseMemoryUsed",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "CaseMemoryAttempted",
                table: "ai_reply_context_snapshots");

            migrationBuilder.DropColumn(
                name: "CaseMemoryCount",
                table: "ai_reply_context_snapshots");

            migrationBuilder.DropColumn(
                name: "CaseMemoryReferences",
                table: "ai_reply_context_snapshots");

            migrationBuilder.DropColumn(
                name: "CaseMemoryUsed",
                table: "ai_reply_context_snapshots");
        }
    }
}
