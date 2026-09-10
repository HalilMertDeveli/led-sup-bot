using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations;

public partial class MultipleCasesAndAiOperatorSummary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_technical_support_cases_conversation_id",
            table: "technical_support_cases");

        migrationBuilder.CreateIndex(
            name: "ix_technical_support_cases_conversation_id",
            table: "technical_support_cases",
            column: "ConversationId");

        migrationBuilder.AddColumn<string>(
            name: "AiOperatorSummary",
            table: "technical_support_cases",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AiOperatorSummary",
            table: "technical_support_cases");

        migrationBuilder.DropIndex(
            name: "ix_technical_support_cases_conversation_id",
            table: "technical_support_cases");

        migrationBuilder.CreateIndex(
            name: "ix_technical_support_cases_conversation_id",
            table: "technical_support_cases",
            column: "ConversationId",
            unique: true);
    }
}
