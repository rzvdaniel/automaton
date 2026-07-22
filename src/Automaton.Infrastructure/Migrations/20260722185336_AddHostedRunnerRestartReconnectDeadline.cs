using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerRestartReconnectDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExpectedRunnerId",
                table: "HostedRunnerDeployments",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "PreviousConnectionId",
                table: "HostedRunnerDeployments",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconnectAfterUtc",
                table: "HostedRunnerDeployments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconnectDeadlineUtc",
                table: "HostedRunnerDeployments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_Status_ReconnectDeadlineUtc",
                table: "HostedRunnerDeployments",
                columns: new[] { "Status", "ReconnectDeadlineUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HostedRunnerDeployments_Status_ReconnectDeadlineUtc",
                table: "HostedRunnerDeployments");

            migrationBuilder.DropColumn(
                name: "ExpectedRunnerId",
                table: "HostedRunnerDeployments");

            migrationBuilder.DropColumn(
                name: "PreviousConnectionId",
                table: "HostedRunnerDeployments");

            migrationBuilder.DropColumn(
                name: "ReconnectAfterUtc",
                table: "HostedRunnerDeployments");

            migrationBuilder.DropColumn(
                name: "ReconnectDeadlineUtc",
                table: "HostedRunnerDeployments");
        }
    }
}
