using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWebAppReviewAndAccessStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccessStatus",
                table: "WebApps",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "active")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "WebApps",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "approved")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("""
                UPDATE WebApps
                SET ReviewStatus = CASE
                        WHEN MarketplaceStatus = 'banned' THEN 'flagged'
                        ELSE 'approved'
                    END,
                    AccessStatus = CASE
                        WHEN MarketplaceStatus = 'hidden' THEN 'hidden'
                        WHEN MarketplaceStatus = 'banned' THEN 'disabled'
                        ELSE 'active'
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsPublished_ReviewStatus_AccessStatus_Category",
                table: "WebApps",
                columns: new[] { "IsPublished", "ReviewStatus", "AccessStatus", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebApps_IsPublished_ReviewStatus_AccessStatus_Category",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "AccessStatus",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "WebApps");
        }
    }
}
