using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineRunnerStorageTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MachineStorageAvailableBytes",
                table: "Runners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MachineStorageMeasuredAtUtc",
                table: "Runners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineStorageMeasurementError",
                table: "Runners",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MachineStorageMeasurementStatus",
                table: "Runners",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "MachineStorageTotalBytes",
                table: "Runners",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MachineStorageAvailableBytes",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "MachineStorageMeasuredAtUtc",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "MachineStorageMeasurementError",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "MachineStorageMeasurementStatus",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "MachineStorageTotalBytes",
                table: "Runners");
        }
    }
}
