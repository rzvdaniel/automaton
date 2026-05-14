using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceModerationAndCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MarketplaceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Slug = table.Column<string>(type: "varchar(140)", maxLength: 140, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceCategories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MarketplaceModerationReason",
                table: "WebApps",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "MarketplaceModeratedAt",
                table: "WebApps",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MarketplaceModeratedByUserId",
                table: "WebApps",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarketplaceStatus",
                table: "WebApps",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "approved")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCategories_IsActive_SortOrder",
                table: "MarketplaceCategories",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCategories_Name",
                table: "MarketplaceCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCategories_Slug",
                table: "MarketplaceCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsPublished_MarketplaceStatus_Category",
                table: "WebApps",
                columns: new[] { "IsPublished", "MarketplaceStatus", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MarketplaceCategories");

            migrationBuilder.DropIndex(
                name: "IX_WebApps_IsPublished_MarketplaceStatus_Category",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "MarketplaceModerationReason",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "MarketplaceModeratedAt",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "MarketplaceModeratedByUserId",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "MarketplaceStatus",
                table: "WebApps");
        }
    }
}
