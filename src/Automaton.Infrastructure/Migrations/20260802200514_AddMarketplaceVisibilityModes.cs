using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceVisibilityModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CatalogVisibility",
                table: "WebApps",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "unlisted")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MarketplacePreviewMode",
                table: "WebApps",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "launchable")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RuntimeVisibility",
                table: "WebApps",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "private")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("""
                UPDATE WebApps
                SET RuntimeVisibility = 'public',
                    CatalogVisibility = 'listed',
                    MarketplacePreviewMode = 'launchable'
                WHERE IsPublished = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_CatalogVisibility_ReviewStatus_AccessStatus_Category",
                table: "WebApps",
                columns: new[] { "CatalogVisibility", "ReviewStatus", "AccessStatus", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebApps_CatalogVisibility_ReviewStatus_AccessStatus_Category",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "CatalogVisibility",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "MarketplacePreviewMode",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "RuntimeVisibility",
                table: "WebApps");
        }
    }
}
