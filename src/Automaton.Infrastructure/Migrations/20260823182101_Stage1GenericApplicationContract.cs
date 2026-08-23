using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Stage1GenericApplicationContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FrontendDeployed",
                table: "AppDeploymentTargets",
                newName: "RuntimeDeployed");

            migrationBuilder.Sql(
                "UPDATE `AppDeploymentTargets` SET `RuntimeDeployed` = 1 WHERE `BackendDeployed` = 1");

            migrationBuilder.DropColumn(
                name: "BackendDeployed",
                table: "AppDeploymentTargets");

            migrationBuilder.AddColumn<string>(
                name: "ApplicationContractJson",
                table: "WebApps",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "UPDATE `WebApps` SET `ApplicationContractJson` = '{\"build\":{\"command\":null,\"outputDirectory\":null,\"timeoutSeconds\":900},\"runtime\":{\"command\":null,\"port\":8080},\"health\":{\"path\":null,\"port\":null,\"startupTimeoutSeconds\":60},\"storage\":{\"dataDirectory\":\"data\",\"mounts\":[]}}' WHERE `ApplicationContractJson` IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "ApplicationContractJson",
                table: "WebApps",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicationContractJson",
                table: "WebApps");

            migrationBuilder.RenameColumn(
                name: "RuntimeDeployed",
                table: "AppDeploymentTargets",
                newName: "FrontendDeployed");

            migrationBuilder.AddColumn<bool>(
                name: "BackendDeployed",
                table: "AppDeploymentTargets",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
