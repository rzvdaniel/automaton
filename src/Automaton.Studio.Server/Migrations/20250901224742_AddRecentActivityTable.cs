using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRecentActivityTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 9, 2, 0, 47, 41, 643, DateTimeKind.Local).AddTicks(5060),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 3, 11, 9, 34, 45, 188, DateTimeKind.Local).AddTicks(6573));

            migrationBuilder.CreateTable(
                name: "RecentActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ActivityType = table.Column<int>(type: "int", nullable: false),
                    EntityName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    EntityId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecentActivities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RecentActivities_CreatedAt",
                table: "RecentActivities",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecentActivities_UserId",
                table: "RecentActivities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentActivities_UserId_CreatedAt",
                table: "RecentActivities",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecentActivities");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "RefreshTokens",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(2025, 3, 11, 9, 34, 45, 188, DateTimeKind.Local).AddTicks(6573),
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValue: new DateTime(2025, 9, 2, 0, 47, 41, 643, DateTimeKind.Local).AddTicks(5060));
        }
    }
}
