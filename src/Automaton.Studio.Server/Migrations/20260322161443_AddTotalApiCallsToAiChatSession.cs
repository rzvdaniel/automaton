using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTotalApiCallsToAiChatSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppVersionSteps_AppVersions_AppVersionId1",
                table: "AppVersionSteps");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectRunners_Runners_RunnerId1",
                table: "ProjectRunners");

            migrationBuilder.DropIndex(
                name: "IX_ProjectRunners_RunnerId1",
                table: "ProjectRunners");

            migrationBuilder.DropIndex(
                name: "IX_AppVersionSteps_AppVersionId1",
                table: "AppVersionSteps");

            migrationBuilder.DropColumn(
                name: "RunnerId1",
                table: "ProjectRunners");

            migrationBuilder.DropColumn(
                name: "AppVersionId1",
                table: "AppVersionSteps");

            migrationBuilder.AddColumn<int>(
                name: "TotalApiCalls",
                table: "AiChatSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalApiCalls",
                table: "AiChatSessions");

            migrationBuilder.AddColumn<Guid>(
                name: "RunnerId1",
                table: "ProjectRunners",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "AppVersionId1",
                table: "AppVersionSteps",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRunners_RunnerId1",
                table: "ProjectRunners",
                column: "RunnerId1");

            migrationBuilder.CreateIndex(
                name: "IX_AppVersionSteps_AppVersionId1",
                table: "AppVersionSteps",
                column: "AppVersionId1");

            migrationBuilder.AddForeignKey(
                name: "FK_AppVersionSteps_AppVersions_AppVersionId1",
                table: "AppVersionSteps",
                column: "AppVersionId1",
                principalTable: "AppVersions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectRunners_Runners_RunnerId1",
                table: "ProjectRunners",
                column: "RunnerId1",
                principalTable: "Runners",
                principalColumn: "Id");
        }
    }
}
