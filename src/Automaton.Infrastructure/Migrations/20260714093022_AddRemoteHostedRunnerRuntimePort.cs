using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteHostedRunnerRuntimePort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RuntimePort",
                table: "HostedRunners",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_RunnerHostId_RuntimePort",
                table: "HostedRunners",
                columns: new[] { "RunnerHostId", "RuntimePort" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HostedRunners_RunnerHostId_RuntimePort",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "RuntimePort",
                table: "HostedRunners");
        }
    }
}
