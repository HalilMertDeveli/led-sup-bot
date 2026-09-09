using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhaseFolderPackageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_panel_file_package_assets_active_type",
                table: "panel_file_package_assets");

            migrationBuilder.DropIndex(
                name: "ux_panel_file_package_assets_version",
                table: "panel_file_package_assets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImportedAt",
                table: "panel_file_packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalFolderName",
                table: "panel_file_packages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelativePath",
                table: "panel_file_package_assets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_panel_file_package_assets_active_path",
                table: "panel_file_package_assets",
                columns: new[] { "PanelFilePackageId", "RelativePath" },
                unique: true,
                filter: "\"Status\" = 'Active' AND \"RelativePath\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_panel_file_package_assets_active_type",
                table: "panel_file_package_assets",
                columns: new[] { "PanelFilePackageId", "AssetType" },
                unique: true,
                filter: "\"Status\" = 'Active' AND \"RelativePath\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_panel_file_package_assets_folder_version",
                table: "panel_file_package_assets",
                columns: new[] { "PanelFilePackageId", "RelativePath", "Version" },
                unique: true,
                filter: "\"RelativePath\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_panel_file_package_assets_version",
                table: "panel_file_package_assets",
                columns: new[] { "PanelFilePackageId", "AssetType", "Version" },
                unique: true,
                filter: "\"RelativePath\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_panel_file_package_assets_active_path",
                table: "panel_file_package_assets");

            migrationBuilder.DropIndex(
                name: "ux_panel_file_package_assets_active_type",
                table: "panel_file_package_assets");

            migrationBuilder.DropIndex(
                name: "ux_panel_file_package_assets_folder_version",
                table: "panel_file_package_assets");

            migrationBuilder.DropIndex(
                name: "ux_panel_file_package_assets_version",
                table: "panel_file_package_assets");

            migrationBuilder.DropColumn(
                name: "ImportedAt",
                table: "panel_file_packages");

            migrationBuilder.DropColumn(
                name: "OriginalFolderName",
                table: "panel_file_packages");

            migrationBuilder.DropColumn(
                name: "RelativePath",
                table: "panel_file_package_assets");

            migrationBuilder.CreateIndex(
                name: "ux_panel_file_package_assets_active_type",
                table: "panel_file_package_assets",
                columns: new[] { "PanelFilePackageId", "AssetType" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_panel_file_package_assets_version",
                table: "panel_file_package_assets",
                columns: new[] { "PanelFilePackageId", "AssetType", "Version" },
                unique: true);
        }
    }
}
