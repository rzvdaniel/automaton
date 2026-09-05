using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivilegedHostAccessApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PrivilegedHostAccessApproved",
                table: "AppDeploymentTargets",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PrivilegedHostAccessApprovedAtUtc",
                table: "AppDeploymentTargets",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrivilegedHostAccessApprovedByUserId",
                table: "AppDeploymentTargets",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.Sql("""
                UPDATE AppDeploymentTargets target
                INNER JOIN Runners runner ON runner.Id = target.ExecutionRunnerId
                SET target.PrivilegedHostAccessApproved = TRUE,
                    target.PrivilegedHostAccessApprovedAtUtc = UTC_TIMESTAMP(6)
                WHERE LOWER(runner.ApplicationProfile) = 'privilegedhost'
                  AND LOWER(runner.Purpose) <> 'infrastructure'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrivilegedHostAccessApproved",
                table: "AppDeploymentTargets");

            migrationBuilder.DropColumn(
                name: "PrivilegedHostAccessApprovedAtUtc",
                table: "AppDeploymentTargets");

            migrationBuilder.DropColumn(
                name: "PrivilegedHostAccessApprovedByUserId",
                table: "AppDeploymentTargets");
        }
    }
}
