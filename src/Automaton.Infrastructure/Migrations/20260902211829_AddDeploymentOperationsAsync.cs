using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentOperationsAsync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveDeploymentOperationId",
                table: "AppDeploymentTargets",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "DeploymentOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    WebAppId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TargetSlug = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PackagePath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Clean = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExecutorInstanceId = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResultJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentOperations_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_ActiveDeploymentOperationId",
                table: "AppDeploymentTargets",
                column: "ActiveDeploymentOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentOperations_WebAppId_CreatedAtUtc",
                table: "DeploymentOperations",
                columns: new[] { "WebAppId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentOperations_WebAppId_Id",
                table: "DeploymentOperations",
                columns: new[] { "WebAppId", "Id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentOperations");

            migrationBuilder.DropIndex(
                name: "IX_AppDeploymentTargets_ActiveDeploymentOperationId",
                table: "AppDeploymentTargets");

            migrationBuilder.DropColumn(
                name: "ActiveDeploymentOperationId",
                table: "AppDeploymentTargets");
        }
    }
}
