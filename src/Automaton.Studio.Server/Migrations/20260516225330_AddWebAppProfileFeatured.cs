using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWebAppProfileFeatured : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsProfileFeatured",
                table: "WebApps",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsProfileFeatured_IsPublished",
                table: "WebApps",
                columns: new[] { "IsProfileFeatured", "IsPublished" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebApps_IsProfileFeatured_IsPublished",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "IsProfileFeatured",
                table: "WebApps");
        }
    }
}
