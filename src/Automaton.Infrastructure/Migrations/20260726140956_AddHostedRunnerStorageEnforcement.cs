using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerStorageEnforcement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnforcementState",
                table: "HostedRunners",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "None")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodEndsAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastEnforcementReason",
                table: "HostedRunners",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastNotificationAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverLimitSinceUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveredAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnforcementState",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "GracePeriodEndsAtUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "LastEnforcementReason",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "LastNotificationAtUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "OverLimitSinceUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "RecoveredAtUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "SuspendedAtUtc",
                table: "HostedRunners");

        }
    }
}
