using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerOperationExecutionClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HostedRunnerOperationId",
                table: "AppModuleExecutions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_HostedRunnerOperationId",
                table: "AppModuleExecutions",
                column: "HostedRunnerOperationId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AppModuleExecutions_HostedRunnerDeployments_HostedRunnerOper~",
                table: "AppModuleExecutions",
                column: "HostedRunnerOperationId",
                principalTable: "HostedRunnerDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppModuleExecutions_HostedRunnerDeployments_HostedRunnerOper~",
                table: "AppModuleExecutions");

            migrationBuilder.DropIndex(
                name: "IX_AppModuleExecutions_HostedRunnerOperationId",
                table: "AppModuleExecutions");

            migrationBuilder.DropColumn(
                name: "HostedRunnerOperationId",
                table: "AppModuleExecutions");
        }
    }
}
