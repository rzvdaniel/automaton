using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class WebAppImage01 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseBackgroundImage",
                table: "WebApps");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 11, 11, 11, 29, 45, 346, DateTimeKind.Local).AddTicks(9609),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 11, 11, 10, 19, 40, 96, DateTimeKind.Local).AddTicks(8553));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                oldDefaultValue: new DateTime(2025, 11, 11, 11, 29, 45, 346, DateTimeKind.Local).AddTicks(9609));
        }
    }
}
