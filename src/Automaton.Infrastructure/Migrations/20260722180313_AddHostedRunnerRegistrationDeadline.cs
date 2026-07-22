using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerRegistrationDeadline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RegistrationDeadlineUtc",
                table: "HostedRunnerDeployments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_Status_RegistrationDeadlineUtc",
                table: "HostedRunnerDeployments",
                columns: new[] { "Status", "RegistrationDeadlineUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HostedRunnerDeployments_Status_RegistrationDeadlineUtc",
                table: "HostedRunnerDeployments");

            migrationBuilder.DropColumn(
                name: "RegistrationDeadlineUtc",
                table: "HostedRunnerDeployments");
        }
    }
}
