using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppVersionSourceReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "AppVersions",
                type: "varchar(160)",
                maxLength: 160,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AppVersions",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reason",
                table: "AppVersions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AppVersions");
        }
    }
}
