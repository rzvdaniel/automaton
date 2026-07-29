using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppDeploymentTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppDeploymentTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    WebAppId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Slug = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExecutionRunnerId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DefaultConstraintKey = table.Column<byte>(type: "tinyint unsigned", nullable: true),
                    FrontendDeployed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    BackendDeployed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastDeploymentAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    LastSuccessfulDeploymentAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDeploymentTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppDeploymentTargets_Runners_ExecutionRunnerId",
                        column: x => x.ExecutionRunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppDeploymentTargets_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_ExecutionRunnerId",
                table: "AppDeploymentTargets",
                column: "ExecutionRunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_WebAppId_DefaultConstraintKey",
                table: "AppDeploymentTargets",
                columns: new[] { "WebAppId", "DefaultConstraintKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_WebAppId_Slug",
                table: "AppDeploymentTargets",
                columns: new[] { "WebAppId", "Slug" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO AppDeploymentTargets (
                    Id,
                    WebAppId,
                    Name,
                    Slug,
                    ExecutionRunnerId,
                    IsDefault,
                    DefaultConstraintKey,
                    FrontendDeployed,
                    BackendDeployed,
                    Status,
                    CreatedAt,
                    UpdatedAt)
                SELECT
                    UUID(),
                    Id,
                    'Default',
                    'default',
                    ExecutionRunnerId,
                    TRUE,
                    1,
                    FALSE,
                    FALSE,
                    CASE
                        WHEN ExecutionRunnerId IS NULL THEN 'Unassigned'
                        ELSE 'NotDeployed'
                    END,
                    UTC_TIMESTAMP(6),
                    UTC_TIMESTAMP(6)
                FROM WebApps;
                """);

            migrationBuilder.DropColumn(
                name: "ExecutionRunnerId",
                table: "WebApps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionRunnerId",
                table: "WebApps",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.Sql(
                """
                UPDATE WebApps app
                INNER JOIN AppDeploymentTargets target
                    ON target.WebAppId = app.Id
                    AND target.IsDefault = TRUE
                SET app.ExecutionRunnerId = target.ExecutionRunnerId;
                """);

            migrationBuilder.DropTable(
                name: "AppDeploymentTargets");
        }
    }
}
