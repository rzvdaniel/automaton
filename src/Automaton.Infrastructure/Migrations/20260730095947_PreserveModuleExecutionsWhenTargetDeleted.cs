using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveModuleExecutionsWhenTargetDeleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppModuleExecutions_AppDeploymentTargets_DeploymentTargetId",
                table: "AppModuleExecutions");

            migrationBuilder.AddForeignKey(
                name: "FK_AppModuleExecutions_AppDeploymentTargets_DeploymentTargetId",
                table: "AppModuleExecutions",
                column: "DeploymentTargetId",
                principalTable: "AppDeploymentTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppModuleExecutions_AppDeploymentTargets_DeploymentTargetId",
                table: "AppModuleExecutions");

            migrationBuilder.AddForeignKey(
                name: "FK_AppModuleExecutions_AppDeploymentTargets_DeploymentTargetId",
                table: "AppModuleExecutions",
                column: "DeploymentTargetId",
                principalTable: "AppDeploymentTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
