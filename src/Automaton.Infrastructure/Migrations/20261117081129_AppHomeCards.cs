using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppHomeCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppHomeCards",
                columns: table => new
                {
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastFailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppHomeCards", x => x.AppId);
                    table.ForeignKey(
                        name: "FK_AppHomeCards_WebApps_AppId",
                        column: x => x.AppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppHomeCards");
        }
    }
}
