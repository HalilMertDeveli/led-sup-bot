using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhaseGI1WebGroundingMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WebGrounded",
                table: "ai_training_examples",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WebSearchAttempted",
                table: "ai_training_examples",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WebSearchUsed",
                table: "ai_training_examples",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WebSourceCount",
                table: "ai_training_examples",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WebSourceReferences",
                table: "ai_training_examples",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "WebGrounded",
                table: "ai_reply_context_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WebSearchAttempted",
                table: "ai_reply_context_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WebSearchUsed",
                table: "ai_reply_context_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WebSourceCount",
                table: "ai_reply_context_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WebSourceReferences",
                table: "ai_reply_context_snapshots",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebGrounded",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "WebSearchAttempted",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "WebSearchUsed",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "WebSourceCount",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "WebSourceReferences",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "WebGrounded",
                table: "ai_reply_context_snapshots");

            migrationBuilder.DropColumn(
                name: "WebSearchAttempted",
                table: "ai_reply_context_snapshots");

            migrationBuilder.DropColumn(
                name: "WebSearchUsed",
                table: "ai_reply_context_snapshots");

            migrationBuilder.DropColumn(
                name: "WebSourceCount",
                table: "ai_reply_context_snapshots");

            migrationBuilder.DropColumn(
                name: "WebSourceReferences",
                table: "ai_reply_context_snapshots");
        }
    }
}
