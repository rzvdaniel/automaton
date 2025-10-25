using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddWebAppPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedRoles",
                table: "WebApps",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "WebApps",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresAuthentication",
                table: "WebApps",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "WebApps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 10, 25, 18, 26, 36, 401, DateTimeKind.Local).AddTicks(5620),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 10, 20, 10, 28, 53, 972, DateTimeKind.Local).AddTicks(6400));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedRoles",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "RequiresAuthentication",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "WebApps");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 10, 20, 10, 28, 53, 972, DateTimeKind.Local).AddTicks(6400),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 10, 25, 18, 26, 36, 401, DateTimeKind.Local).AddTicks(5620));
        }
    }
}
