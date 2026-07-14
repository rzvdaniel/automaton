using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstallationId",
                table: "Runners",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RunnerEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RunnerHostId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RequestedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExpectedPurpose = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedCapabilitiesJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EnrollmentTokenHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InstallationId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RunnerId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    FailureReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerEnrollments_RunnerHosts_RunnerHostId",
                        column: x => x.RunnerHostId,
                        principalTable: "RunnerHosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Runners_InstallationId",
                table: "Runners",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_EnrollmentTokenHash",
                table: "RunnerEnrollments",
                column: "EnrollmentTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_RunnerHostId",
                table: "RunnerEnrollments",
                column: "RunnerHostId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_RunnerHostId_Status",
                table: "RunnerEnrollments",
                columns: new[] { "RunnerHostId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunnerEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_Runners_InstallationId",
                table: "Runners");

            migrationBuilder.DropColumn(
                name: "InstallationId",
                table: "Runners");
        }
    }
}
