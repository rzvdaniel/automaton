using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppInstalledFromSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstalledFromCommit",
                table: "WebApps",
                type: "citext",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstalledFromFolder",
                table: "WebApps",
                type: "citext",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstalledFromRepo",
                table: "WebApps",
                type: "citext",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstalledFromCommit",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "InstalledFromFolder",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "InstalledFromRepo",
                table: "WebApps");
        }
    }
}
