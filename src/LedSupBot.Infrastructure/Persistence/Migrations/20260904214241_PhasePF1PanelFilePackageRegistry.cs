using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LedSupBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhasePF1PanelFilePackageRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "panel_file_packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PixelPitch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PanelCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SecondaryCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CanonicalKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Manufacturer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_file_packages", x => x.Id);
                    table.CheckConstraint("ck_panel_file_packages_canonical_identity", "\"CanonicalKey\" = \"PixelPitch\" || '-' || \"PanelCode\" || '-' || \"SecondaryCode\"");
                });

            migrationBuilder.CreateTable(
                name: "panel_file_package_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PanelFilePackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_panel_file_package_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_panel_file_package_assets_panel_file_packages_PanelFilePack~",
                        column: x => x.PanelFilePackageId,
                        principalTable: "panel_file_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_panel_file_package_assets_sha256",
                table: "panel_file_package_assets",
                column: "Sha256");

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

            migrationBuilder.CreateIndex(
                name: "ix_panel_file_packages_status",
                table: "panel_file_packages",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "ux_panel_file_packages_canonical_key",
                table: "panel_file_packages",
                column: "CanonicalKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "panel_file_package_assets");

            migrationBuilder.DropTable(
                name: "panel_file_packages");
        }
    }
}
