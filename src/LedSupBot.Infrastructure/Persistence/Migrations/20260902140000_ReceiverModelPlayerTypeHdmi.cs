using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations;

public partial class ReceiverModelPlayerTypeHdmi : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ReceiverModel",
            table: "receiver_informations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PlayerType",
            table: "technical_support_cases",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "ProcessorHdmiConnected",
            table: "technical_support_cases",
            type: "boolean",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReceiverModel",
            table: "receiver_informations");

        migrationBuilder.DropColumn(
            name: "PlayerType",
            table: "technical_support_cases");

        migrationBuilder.DropColumn(
            name: "ProcessorHdmiConnected",
            table: "technical_support_cases");
    }
}
