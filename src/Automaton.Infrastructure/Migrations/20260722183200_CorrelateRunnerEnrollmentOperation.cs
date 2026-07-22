using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrelateRunnerEnrollmentOperation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "RunnerEnrollments",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_OperationId",
                table: "RunnerEnrollments",
                column: "OperationId");

            migrationBuilder.AddForeignKey(
                name: "FK_RunnerEnrollments_HostedRunnerDeployments_OperationId",
                table: "RunnerEnrollments",
                column: "OperationId",
                principalTable: "HostedRunnerDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RunnerEnrollments_HostedRunnerDeployments_OperationId",
                table: "RunnerEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_RunnerEnrollments_OperationId",
                table: "RunnerEnrollments");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "RunnerEnrollments");
        }
    }
}
