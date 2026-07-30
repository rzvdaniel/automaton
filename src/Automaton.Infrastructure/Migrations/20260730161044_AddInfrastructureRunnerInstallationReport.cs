using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInfrastructureRunnerInstallationReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiCompatibilityVersion",
                table: "Runners",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Architecture",
                table: "Runners",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ContainerHealth",
                table: "Runners",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ContainerSpecificationVersion",
                table: "Runners",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageDigest",
                table: "Runners",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OperatingSystem",
                table: "Runners",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiCompatibilityVersion",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "Architecture",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "ContainerHealth",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "ContainerSpecificationVersion",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "ImageDigest",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "OperatingSystem",
                table: "Runners");
        }
    }
}
