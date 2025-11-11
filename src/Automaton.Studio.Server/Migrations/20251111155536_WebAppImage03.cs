using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class WebAppImage03 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundColor",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "BackgroundImageOpacity",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "BackgroundImageUrl",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "TitleFontColor",
                table: "WebApps");

            migrationBuilder.AddColumn<string>(
                name: "Appearance",
                table: "WebApps",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 11, 11, 16, 55, 35, 479, DateTimeKind.Local).AddTicks(7532),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 11, 11, 16, 24, 40, 484, DateTimeKind.Local).AddTicks(1618));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Appearance",
                table: "WebApps");

            migrationBuilder.AddColumn<string>(
                name: "BackgroundColor",
                table: "WebApps",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "BackgroundImageOpacity",
                table: "WebApps",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "BackgroundImageUrl",
                table: "WebApps",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "TitleFontColor",
                table: "WebApps",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 11, 11, 16, 24, 40, 484, DateTimeKind.Local).AddTicks(1618),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 11, 11, 16, 55, 35, 479, DateTimeKind.Local).AddTicks(7532));
        }
    }
}
