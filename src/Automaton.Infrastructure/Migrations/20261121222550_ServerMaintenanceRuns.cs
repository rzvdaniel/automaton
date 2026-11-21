using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServerMaintenanceRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerMaintenanceRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedBy = table.Column<string>(type: "citext", maxLength: 200, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    UpgradedCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerMaintenanceRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerMaintenanceRuns_Kind_RunnerId_StartedAtUtc",
                table: "ServerMaintenanceRuns",
                columns: new[] { "Kind", "RunnerId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerMaintenanceRuns_StartedAtUtc",
                table: "ServerMaintenanceRuns",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerMaintenanceRuns");
        }
    }
}
