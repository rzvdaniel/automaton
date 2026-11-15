using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AppCustomDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCustomDomains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "citext", maxLength: 253, nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCustomDomains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppCustomDomains_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCustomDomains_Host",
                table: "AppCustomDomains",
                column: "Host",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCustomDomains_WebAppId",
                table: "AppCustomDomains",
                column: "WebAppId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCustomDomains");
        }
    }
}
