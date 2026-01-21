using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ApplicationUser public profile fields
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Bio",
                table: "AspNetUsers",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "AspNetUsers",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            // WebApp public display fields
            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "WebApps",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "WebApps",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "WebApps",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            // Index for efficient public apps queries by user
            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsPublished",
                table: "WebApps",
                column: "IsPublished");

            // Remove deprecated Appearance JSON column (replaced by ThumbnailUrl)
            migrationBuilder.DropColumn(
                name: "Appearance",
                table: "WebApps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore Appearance column
            migrationBuilder.AddColumn<string>(
                name: "Appearance",
                table: "WebApps",
                type: "longtext",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.DropIndex(
                name: "IX_WebApps_IsPublished",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Bio",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "WebApps");
        }
    }
}
