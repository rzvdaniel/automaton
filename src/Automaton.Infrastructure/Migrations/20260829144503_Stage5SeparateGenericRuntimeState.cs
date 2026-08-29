using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage5SeparateGenericRuntimeState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GenericRuntimeDeployed",
                table: "AppDeploymentTargets",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GenericRuntimeDeployed",
                table: "AppDeploymentTargets");
        }
    }
}
