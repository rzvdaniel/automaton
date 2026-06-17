using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebAppForkLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ForkedFromAppId",
                table: "WebApps",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "ForkedFromAppName",
                table: "WebApps",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ForkedFromAuthorName",
                table: "WebApps",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "ForkedFromAuthorUserId",
                table: "WebApps",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_ForkedFromAppId",
                table: "WebApps",
                column: "ForkedFromAppId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebApps_ForkedFromAppId",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "ForkedFromAppId",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "ForkedFromAppName",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "ForkedFromAuthorName",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "ForkedFromAuthorUserId",
                table: "WebApps");
        }
    }
}
