using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerHostProvisioningOperationMaterial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CloudInitGeneratedAtUtc",
                table: "RunnerHostProvisioningOperations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudInitTemplateVersion",
                table: "RunnerHostProvisioningOperations",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "EnrollmentId",
                table: "RunnerHostProvisioningOperations",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "RunnerHostProvisioningOperations",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloudInitGeneratedAtUtc",
                table: "RunnerHostProvisioningOperations");

            migrationBuilder.DropColumn(
                name: "CloudInitTemplateVersion",
                table: "RunnerHostProvisioningOperations");

            migrationBuilder.DropColumn(
                name: "EnrollmentId",
                table: "RunnerHostProvisioningOperations");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "RunnerHostProvisioningOperations");
        }
    }
}
