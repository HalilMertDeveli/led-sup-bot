using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhasePF8StructuredPanelRecognitionTeacherMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PanelRecognitionSnapshot",
                table: "ai_training_examples",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredPanelIdentitySnapshot",
                table: "ai_training_examples",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PanelRecognitionSnapshot",
                table: "ai_training_examples");

            migrationBuilder.DropColumn(
                name: "PreferredPanelIdentitySnapshot",
                table: "ai_training_examples");
        }
    }
}
