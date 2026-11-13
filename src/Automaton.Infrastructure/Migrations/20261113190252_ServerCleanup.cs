using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServerCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerCleanupRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerName = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Result = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    BytesFreed = table.Column<long>(type: "bigint", nullable: false),
                    Trigger = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerCleanupRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServerCleanupSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MinutesAfterMidnightUtc = table.Column<int>(type: "integer", nullable: false),
                    KeepDays = table.Column<int>(type: "integer", nullable: false),
                    JournalLimitMb = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerCleanupSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerCleanupRuns_StartedAtUtc",
                table: "ServerCleanupRuns",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerCleanupRuns");

            migrationBuilder.DropTable(
                name: "ServerCleanupSettings");
        }
    }
}
