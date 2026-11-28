using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppDataGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppDataGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    GranteeAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Volume = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    ReadWrite = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDataGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppDataGrants_WebApps_GranteeAppId",
                        column: x => x.GranteeAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppDataGrants_WebApps_SourceAppId",
                        column: x => x.SourceAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDataGrants_GranteeAppId",
                table: "AppDataGrants",
                column: "GranteeAppId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDataGrants_SourceAppId_GranteeAppId_Volume",
                table: "AppDataGrants",
                columns: new[] { "SourceAppId", "GranteeAppId", "Volume" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppDataGrants");
        }
    }
}
