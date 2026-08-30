using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage8NameLegacyControllerRuntimeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RuntimeDeployed",
                table: "AppDeploymentTargets",
                newName: "LegacyControllerRuntimeDeployed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LegacyControllerRuntimeDeployed",
                table: "AppDeploymentTargets",
                newName: "RuntimeDeployed");
        }
    }
}
