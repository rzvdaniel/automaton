using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppTeamSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SharedWithProjectId",
                table: "WebApps",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_SharedWithProjectId",
                table: "WebApps",
                column: "SharedWithProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_WebApps_Projects_SharedWithProjectId",
                table: "WebApps",
                column: "SharedWithProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WebApps_Projects_SharedWithProjectId",
                table: "WebApps");

            migrationBuilder.DropIndex(
                name: "IX_WebApps_SharedWithProjectId",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "SharedWithProjectId",
                table: "WebApps");
        }
    }
}
