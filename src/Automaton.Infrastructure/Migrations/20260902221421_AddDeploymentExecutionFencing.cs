using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentExecutionFencing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionAttemptId",
                table: "DeploymentOperations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "DeploymentOperations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveDeploymentExecutionAttemptId",
                table: "AppDeploymentTargets",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_ActiveDeploymentExecutionAttemptId",
                table: "AppDeploymentTargets",
                column: "ActiveDeploymentExecutionAttemptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppDeploymentTargets_ActiveDeploymentExecutionAttemptId",
                table: "AppDeploymentTargets");

            migrationBuilder.DropColumn(
                name: "ExecutionAttemptId",
                table: "DeploymentOperations");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "DeploymentOperations");

            migrationBuilder.DropColumn(
                name: "ActiveDeploymentExecutionAttemptId",
                table: "AppDeploymentTargets");
        }
    }
}
