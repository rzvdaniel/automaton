using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class WebAppImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<bool>(
                name: "UseBackgroundImage",
                table: "WebApps",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 11, 11, 10, 19, 40, 96, DateTimeKind.Local).AddTicks(8553),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 11, 11, 10, 13, 44, 909, DateTimeKind.Local).AddTicks(1149));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                name: "UseBackgroundImage",
                table: "WebApps");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 11, 11, 10, 13, 44, 909, DateTimeKind.Local).AddTicks(1149),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 11, 11, 10, 19, 40, 96, DateTimeKind.Local).AddTicks(8553));
        }
    }
}
