using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerConnectionSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunnerConnectionSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RunnerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ConnectionId = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DisconnectedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DisconnectReason = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerConnectionSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerConnectionSessions_Runners_RunnerId",
                        column: x => x.RunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerConnectionSessions_ConnectionId",
                table: "RunnerConnectionSessions",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerConnectionSessions_RunnerId",
                table: "RunnerConnectionSessions",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerConnectionSessions_RunnerId_DisconnectedAtUtc",
                table: "RunnerConnectionSessions",
                columns: new[] { "RunnerId", "DisconnectedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerConnectionSessions");
        }
    }
}
