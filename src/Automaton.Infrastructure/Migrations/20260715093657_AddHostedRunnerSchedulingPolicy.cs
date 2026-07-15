using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerSchedulingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HostedRunnerSchedulingEnabled",
                table: "RunnerHosts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_Status_HostedRunnerSchedulingEnabled",
                table: "RunnerHosts",
                columns: new[] { "Status", "HostedRunnerSchedulingEnabled" });

            migrationBuilder.Sql(
                "UPDATE RunnerHosts SET HostedRunnerSchedulingEnabled = 0 WHERE IsLocal = 1 OR Provider = 'local';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RunnerHosts_Status_HostedRunnerSchedulingEnabled",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "HostedRunnerSchedulingEnabled",
                table: "RunnerHosts");
        }
    }
}
