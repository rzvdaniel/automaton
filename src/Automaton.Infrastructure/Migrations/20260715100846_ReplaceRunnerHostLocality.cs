using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRunnerHostLocality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProvisioningTransport",
                table: "RunnerHosts",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "infrastructureRunner")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RuntimeConnectivityMode",
                table: "RunnerHosts",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "publicHostPort")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "UPDATE RunnerHosts SET ProvisioningTransport = 'localDocker', RuntimeConnectivityMode = 'localDockerNetwork' WHERE IsLocal = 1 OR Provider = 'local';");

            migrationBuilder.Sql(
                "UPDATE RunnerHosts SET ProvisioningTransport = 'ssh', RuntimeConnectivityMode = 'publicHostPort' WHERE IsLocal = 0 AND Provider <> 'local';");

            migrationBuilder.DropColumn(
                name: "IsLocal",
                table: "RunnerHosts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProvisioningTransport",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "RuntimeConnectivityMode",
                table: "RunnerHosts");

            migrationBuilder.AddColumn<bool>(
                name: "IsLocal",
                table: "RunnerHosts",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
