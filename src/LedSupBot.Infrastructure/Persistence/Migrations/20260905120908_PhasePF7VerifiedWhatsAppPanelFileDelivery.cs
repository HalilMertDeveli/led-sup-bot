using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhasePF7VerifiedWhatsAppPanelFileDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PanelFileDeliveryStateJson",
                table: "technical_support_cases",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PanelFileDeliveryStateJson",
                table: "technical_support_cases");
        }
    }
}
