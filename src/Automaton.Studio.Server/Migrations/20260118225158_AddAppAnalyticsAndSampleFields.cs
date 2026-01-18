using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAppAnalyticsAndSampleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                table: "WebApps",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSample",
                table: "WebApps",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastViewedAt",
                table: "WebApps",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ViewCount",
                table: "WebApps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsSample_IsPublished",
                table: "WebApps",
                columns: new[] { "IsSample", "IsPublished" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebApps_IsSample_IsPublished",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "IsFeatured",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "IsSample",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "LastViewedAt",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "ViewCount",
                table: "WebApps");
        }
    }
}
