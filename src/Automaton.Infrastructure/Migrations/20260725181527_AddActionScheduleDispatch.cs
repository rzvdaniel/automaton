using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddActionScheduleDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleOccurrenceId",
                table: "AppModuleExecutions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "ActionSchedules",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "ActionSchedules",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ActionScheduleOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ScheduleId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TriggeredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailureSummary = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionScheduleOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionScheduleOccurrences_ActionSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "ActionSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_ScheduleOccurrenceId",
                table: "AppModuleExecutions",
                column: "ScheduleOccurrenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionScheduleOccurrences_IdempotencyKey",
                table: "ActionScheduleOccurrences",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionScheduleOccurrences_ScheduleId_Status_ScheduledForUtc",
                table: "ActionScheduleOccurrences",
                columns: new[] { "ScheduleId", "Status", "ScheduledForUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionScheduleOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_AppModuleExecutions_ScheduleOccurrenceId",
                table: "AppModuleExecutions");

            migrationBuilder.DropColumn(
                name: "ScheduleOccurrenceId",
                table: "AppModuleExecutions");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "ActionSchedules");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "ActionSchedules");
        }
    }
}
