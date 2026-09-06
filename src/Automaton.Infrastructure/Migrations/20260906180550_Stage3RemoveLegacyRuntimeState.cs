using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage3RemoveLegacyRuntimeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LegacyControllerRuntimeDeployed",
                table: "AppDeploymentTargets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LegacyControllerRuntimeDeployed",
                table: "AppDeploymentTargets",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
