using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerHostProvisioningLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InfrastructureRunnerEnrolledAtUtc",
                table: "RunnerHosts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderImageId",
                table: "RunnerHosts",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProviderResourceId",
                table: "RunnerHosts",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProviderServerType",
                table: "RunnerHosts",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningFailureCode",
                table: "RunnerHosts",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningFailureMessage",
                table: "RunnerHosts",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ProvisioningStartedAtUtc",
                table: "RunnerHosts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningStatus",
                table: "RunnerHosts",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "manual")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidatedAtUtc",
                table: "RunnerHosts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_ProvisioningStatus",
                table: "RunnerHosts",
                column: "ProvisioningStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RunnerHosts_ProvisioningStatus",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "InfrastructureRunnerEnrolledAtUtc",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ProviderImageId",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ProviderResourceId",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ProviderServerType",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ProvisioningFailureCode",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ProvisioningFailureMessage",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ProvisioningStartedAtUtc",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ProvisioningStatus",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ValidatedAtUtc",
                table: "RunnerHosts");
        }
    }
}
