using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentTargetToModuleExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppModuleExecutions_AppId_CreatedAtUtc",
                table: "AppModuleExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AppModuleExecutions_AppId_ModuleType_ModuleKey_CreatedAtUtc",
                table: "AppModuleExecutions");

            migrationBuilder.AddColumn<Guid>(
                name: "DeploymentTargetId",
                table: "AppModuleExecutions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.Sql(
                """
                UPDATE AppModuleExecutions executions
                INNER JOIN AppDeploymentTargets targets
                    ON targets.WebAppId = executions.AppId
                    AND targets.IsDefault = 1
                SET executions.DeploymentTargetId = targets.Id
                WHERE executions.RunnerId IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_AppId_DeploymentTargetId_CreatedAtUtc",
                table: "AppModuleExecutions",
                columns: new[] { "AppId", "DeploymentTargetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_AppId_DeploymentTargetId_ModuleType_Modu~",
                table: "AppModuleExecutions",
                columns: new[] { "AppId", "DeploymentTargetId", "ModuleType", "ModuleKey", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_DeploymentTargetId",
                table: "AppModuleExecutions",
                column: "DeploymentTargetId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppModuleExecutions_AppDeploymentTargets_DeploymentTargetId",
                table: "AppModuleExecutions",
                column: "DeploymentTargetId",
                principalTable: "AppDeploymentTargets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppModuleExecutions_AppDeploymentTargets_DeploymentTargetId",
                table: "AppModuleExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AppModuleExecutions_AppId_DeploymentTargetId_CreatedAtUtc",
                table: "AppModuleExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AppModuleExecutions_AppId_DeploymentTargetId_ModuleType_Modu~",
                table: "AppModuleExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AppModuleExecutions_DeploymentTargetId",
                table: "AppModuleExecutions");

            migrationBuilder.DropColumn(
                name: "DeploymentTargetId",
                table: "AppModuleExecutions");

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_AppId_CreatedAtUtc",
                table: "AppModuleExecutions",
                columns: new[] { "AppId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_AppId_ModuleType_ModuleKey_CreatedAtUtc",
                table: "AppModuleExecutions",
                columns: new[] { "AppId", "ModuleType", "ModuleKey", "CreatedAtUtc" });
        }
    }
}
