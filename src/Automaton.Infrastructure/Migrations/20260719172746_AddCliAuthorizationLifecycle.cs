using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCliAuthorizationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                table: "CliAuthorizationRequests",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "CliAuthorizationRequests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CliAuthorizationRequests_ApiKeyId",
                table: "CliAuthorizationRequests",
                column: "ApiKeyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CliAuthorizationRequests_ApiKeyId",
                table: "CliAuthorizationRequests");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                table: "CliAuthorizationRequests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "CliAuthorizationRequests");
        }
    }
}
