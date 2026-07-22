using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerEnrollmentSecretBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HostedRunnerId",
                table: "RunnerEnrollments",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "SecretVersionId",
                table: "RunnerEnrollments",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_HostedRunnerId",
                table: "RunnerEnrollments",
                column: "HostedRunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_HostedRunnerId_Status",
                table: "RunnerEnrollments",
                columns: new[] { "HostedRunnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_SecretVersionId",
                table: "RunnerEnrollments",
                column: "SecretVersionId");

            migrationBuilder.AddForeignKey(
                name: "FK_RunnerEnrollments_HostedRunners_HostedRunnerId",
                table: "RunnerEnrollments",
                column: "HostedRunnerId",
                principalTable: "HostedRunners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RunnerEnrollments_SecretVersions_SecretVersionId",
                table: "RunnerEnrollments",
                column: "SecretVersionId",
                principalTable: "SecretVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RunnerEnrollments_HostedRunners_HostedRunnerId",
                table: "RunnerEnrollments");

            migrationBuilder.DropForeignKey(
                name: "FK_RunnerEnrollments_SecretVersions_SecretVersionId",
                table: "RunnerEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_RunnerEnrollments_HostedRunnerId",
                table: "RunnerEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_RunnerEnrollments_HostedRunnerId_Status",
                table: "RunnerEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_RunnerEnrollments_SecretVersionId",
                table: "RunnerEnrollments");

            migrationBuilder.DropColumn(
                name: "HostedRunnerId",
                table: "RunnerEnrollments");

            migrationBuilder.DropColumn(
                name: "SecretVersionId",
                table: "RunnerEnrollments");
        }
    }
}
