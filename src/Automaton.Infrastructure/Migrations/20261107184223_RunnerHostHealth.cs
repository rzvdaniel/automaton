using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RunnerHostHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MachineHostHealthJson",
                table: "Runners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MachineHostHealthMeasuredAtUtc",
                table: "Runners",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MachineHostHealthJson",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "MachineHostHealthMeasuredAtUtc",
                table: "Runners");
        }
    }
}
