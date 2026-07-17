using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedInfrastructureRunnerEnrollmentKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApiKeyId",
                table: "Runners",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "MaximumUses",
                table: "ApiKeys",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UseCount",
                table: "ApiKeys",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UsedAt",
                table: "ApiKeys",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Runners_ApiKeyId",
                table: "Runners",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UsedAt",
                table: "ApiKeys",
                column: "UsedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Runners_ApiKeys_ApiKeyId",
                table: "Runners",
                column: "ApiKeyId",
                principalTable: "ApiKeys",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Runners_ApiKeys_ApiKeyId",
                table: "Runners");

            migrationBuilder.DropIndex(
                name: "IX_Runners_ApiKeyId",
                table: "Runners");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_UsedAt",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "ApiKeyId",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "MaximumUses",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "UseCount",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "UsedAt",
                table: "ApiKeys");
        }
    }
}
