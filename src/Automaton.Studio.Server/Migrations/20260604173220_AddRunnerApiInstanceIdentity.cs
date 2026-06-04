using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerApiInstanceIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiColor",
                table: "Runners",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ApiInstanceId",
                table: "Runners",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ApiColor",
                table: "RunnerConnectionSessions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ApiInstanceId",
                table: "RunnerConnectionSessions",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerConnectionSessions_ApiInstanceId_DisconnectedAtUtc",
                table: "RunnerConnectionSessions",
                columns: new[] { "ApiInstanceId", "DisconnectedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RunnerConnectionSessions_ApiInstanceId_DisconnectedAtUtc",
                table: "RunnerConnectionSessions");

            migrationBuilder.DropColumn(
                name: "ApiColor",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "ApiInstanceId",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "ApiColor",
                table: "RunnerConnectionSessions");

            migrationBuilder.DropColumn(
                name: "ApiInstanceId",
                table: "RunnerConnectionSessions");
        }
    }
}
