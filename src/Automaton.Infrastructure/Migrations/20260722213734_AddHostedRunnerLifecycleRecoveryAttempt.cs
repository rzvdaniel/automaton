using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerLifecycleRecoveryAttempt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRecoveryAttemptUtc",
                table: "HostedRunnerDeployments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_Status_LastRecoveryAttemptUtc",
                table: "HostedRunnerDeployments",
                columns: new[] { "Status", "LastRecoveryAttemptUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HostedRunnerDeployments_Status_LastRecoveryAttemptUtc",
                table: "HostedRunnerDeployments");

            migrationBuilder.DropColumn(
                name: "LastRecoveryAttemptUtc",
                table: "HostedRunnerDeployments");
        }
    }
}
