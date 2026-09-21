using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerStorageProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StorageProjectId",
                table: "HostedRunners",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_StorageProjectId",
                table: "HostedRunners",
                column: "StorageProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HostedRunners_StorageProjectId",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageProjectId",
                table: "HostedRunners");
        }
    }
}
