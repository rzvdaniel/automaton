using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentTargetCleanupState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CleanupPending",
                table: "AppDeploymentTargets",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCleanupAttemptAt",
                table: "AppDeploymentTargets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastCleanupError",
                table: "AppDeploymentTargets",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CleanupPending",
                table: "AppDeploymentTargets");

            migrationBuilder.DropColumn(
                name: "LastCleanupAttemptAt",
                table: "AppDeploymentTargets");

            migrationBuilder.DropColumn(
                name: "LastCleanupError",
                table: "AppDeploymentTargets");
        }
    }
}
