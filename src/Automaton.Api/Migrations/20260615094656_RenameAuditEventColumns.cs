using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class RenameAuditEventColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PropertiesJson",
                table: "AuditEvents",
                newName: "MetadataJson");

            migrationBuilder.RenameColumn(
                name: "Level",
                table: "AuditEvents",
                newName: "Outcome");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Outcome",
                table: "AuditEvents",
                newName: "Level");

            migrationBuilder.RenameColumn(
                name: "MetadataJson",
                table: "AuditEvents",
                newName: "PropertiesJson");
        }
    }
}
