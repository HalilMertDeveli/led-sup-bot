using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhasePF3PanelIdentityTextRecognition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PanelIdentityStateJson",
                table: "technical_support_cases",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PanelIdentityContextSnapshot",
                table: "ai_reply_context_snapshots",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PanelIdentityStateJson",
                table: "technical_support_cases");

            migrationBuilder.DropColumn(
                name: "PanelIdentityContextSnapshot",
                table: "ai_reply_context_snapshots");
        }
    }
}
