using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInfrastructureRunnerProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "Runners",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Managed")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "InfrastructureRunnerId",
                table: "RunnerHosts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_InfrastructureRunnerId",
                table: "RunnerHosts",
                column: "InfrastructureRunnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_RunnerHosts_Runners_InfrastructureRunnerId",
                table: "RunnerHosts",
                column: "InfrastructureRunnerId",
                principalTable: "Runners",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RunnerHosts_Runners_InfrastructureRunnerId",
                table: "RunnerHosts");

            migrationBuilder.DropIndex(
                name: "IX_RunnerHosts_InfrastructureRunnerId",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "InfrastructureRunnerId",
                table: "RunnerHosts");
        }
    }
}
