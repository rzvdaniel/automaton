using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRunnerHostMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HealthJson",
                table: "RunnerHosts",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatAtUtc",
                table: "RunnerHosts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "RunnerHosts",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PrivateIp",
                table: "RunnerHosts",
                type: "varchar(120)",
                maxLength: 120,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "ReservedCpuCores",
                table: "RunnerHosts",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReservedDiskMb",
                table: "RunnerHosts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReservedMemoryMb",
                table: "RunnerHosts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "InfrastructureWorkflowExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    WorkflowVersionId = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedByUserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TargetRunnerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TargetRunnerHostId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExitCode = table.Column<int>(type: "int", nullable: true),
                    Error = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResultJson = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfrastructureWorkflowExecutions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InfrastructureWorkflowExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExecutionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Stream = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Message = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfrastructureWorkflowExecutionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InfrastructureWorkflowExecutionLogs_InfrastructureWorkflowEx~",
                        column: x => x.ExecutionId,
                        principalTable: "InfrastructureWorkflowExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_Region",
                table: "RunnerHosts",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureWorkflowExecutionLogs_ExecutionId_Sequence",
                table: "InfrastructureWorkflowExecutionLogs",
                columns: new[] { "ExecutionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureWorkflowExecutions_IdempotencyKey",
                table: "InfrastructureWorkflowExecutions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureWorkflowExecutions_TargetRunnerHostId_CreatedA~",
                table: "InfrastructureWorkflowExecutions",
                columns: new[] { "TargetRunnerHostId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureWorkflowExecutions_TargetRunnerId_CreatedAtUtc",
                table: "InfrastructureWorkflowExecutions",
                columns: new[] { "TargetRunnerId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InfrastructureWorkflowExecutionLogs");

            migrationBuilder.DropTable(
                name: "InfrastructureWorkflowExecutions");

            migrationBuilder.DropIndex(
                name: "IX_RunnerHosts_Region",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "HealthJson",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAtUtc",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "PrivateIp",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ReservedCpuCores",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ReservedDiskMb",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "ReservedMemoryMb",
                table: "RunnerHosts");
        }
    }
}
