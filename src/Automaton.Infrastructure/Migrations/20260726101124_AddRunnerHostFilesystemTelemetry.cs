using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerHostFilesystemTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DockerFilesystemAvailableBytes",
                table: "RunnerHosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DockerFilesystemTotalBytes",
                table: "RunnerHosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RootFilesystemAvailableBytes",
                table: "RunnerHosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RootFilesystemTotalBytes",
                table: "RunnerHosts",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DockerFilesystemAvailableBytes",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "DockerFilesystemTotalBytes",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "RootFilesystemAvailableBytes",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "RootFilesystemTotalBytes",
                table: "RunnerHosts");
        }
    }
}
