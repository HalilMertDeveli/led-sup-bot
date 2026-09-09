using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations;

public partial class Phase12IssueSummaryAnyDeskAlert : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AnyDeskAlertDismissedAt",
            table: "technical_support_cases",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AnyDeskAlertSentAt",
            table: "technical_support_cases",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "IssueSummary",
            table: "technical_support_cases",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AnyDeskAlertDismissedAt", table: "technical_support_cases");
        migrationBuilder.DropColumn(name: "AnyDeskAlertSentAt", table: "technical_support_cases");
        migrationBuilder.DropColumn(name: "IssueSummary", table: "technical_support_cases");
    }
}
