using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class LinkedAppSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebAppSteps",
                columns: table => new
                {
                    WebAppId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CustomStepId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LinkedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LinkedBy = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAppSteps", x => new { x.WebAppId, x.CustomStepId });
                    table.ForeignKey(
                        name: "FK_WebAppSteps_AspNetUsers_LinkedBy",
                        column: x => x.LinkedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WebAppSteps_CustomSteps_CustomStepId",
                        column: x => x.CustomStepId,
                        principalTable: "CustomSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebAppSteps_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_WebAppSteps_CustomStepId",
                table: "WebAppSteps",
                column: "CustomStepId");

            migrationBuilder.CreateIndex(
                name: "IX_WebAppSteps_LinkedBy",
                table: "WebAppSteps",
                column: "LinkedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WebAppSteps_WebAppId",
                table: "WebAppSteps",
                column: "WebAppId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebAppSteps");
        }
    }
}
