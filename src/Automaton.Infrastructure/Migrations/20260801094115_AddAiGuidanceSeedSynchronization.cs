using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiGuidanceSeedSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSeedRetired",
                table: "AiGuidanceDocuments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeedSynchronizedAt",
                table: "AiGuidanceDocuments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeedContentHash",
                table: "AiGuidanceDocuments",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "SeedVersion",
                table: "AiGuidanceDocuments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AiGuidanceDocuments",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Admin")
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSeedRetired",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "LastSeedSynchronizedAt",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "SeedContentHash",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "SeedVersion",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AiGuidanceDocuments");
        }
    }
}
