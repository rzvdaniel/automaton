using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributedStorageTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "StorageAvailableBytes",
                table: "RunnerHosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StorageMeasuredAtUtc",
                table: "RunnerHosts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageMeasurementError",
                table: "RunnerHosts",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StorageMeasurementStatus",
                table: "RunnerHosts",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NeverMeasured")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "StorageReceivedAtUtc",
                table: "RunnerHosts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageReserveRequiredBytes",
                table: "RunnerHosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageState",
                table: "RunnerHosts",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "StorageTotalBytes",
                table: "RunnerHosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageUsedBytes",
                table: "RunnerHosts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageAllowanceBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "StorageMeasuredAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageMeasurementError",
                table: "HostedRunners",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StorageMeasurementSource",
                table: "HostedRunners",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "RunnerSelfReported")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StorageMeasurementStatus",
                table: "HostedRunners",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NeverMeasured")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "StorageReceivedAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StorageSelfReportedAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageSelfReportedError",
                table: "HostedRunners",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StorageSelfReportedStatus",
                table: "HostedRunners",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "NeverMeasured")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "StorageSelfReportedSystemBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageSelfReportedTotalBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageSelfReportedUserBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageState",
                table: "HostedRunners",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "StorageSystemBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageTotalBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StorageUsagePercent",
                table: "HostedRunners",
                type: "decimal(7,2)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageUserBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StorageVerificationRequestedAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StorageVerifiedAtUtc",
                table: "HostedRunners",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageVerifiedSystemBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageVerifiedTotalBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StorageVerifiedUserBytes",
                table: "HostedRunners",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HostedRunnerStorageObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    HostedRunnerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MeasuredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SystemBytes = table.Column<long>(type: "bigint", nullable: false),
                    UserBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: false),
                    Success = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Error = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RunnerVersion = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerStorageObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerStorageObservations_HostedRunners_HostedRunnerId",
                        column: x => x.HostedRunnerId,
                        principalTable: "HostedRunners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HostedRunnerStorageVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    HostedRunnerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    IdempotencyKey = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Error = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerStorageVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerStorageVerifications_HostedRunners_HostedRunnerId",
                        column: x => x.HostedRunnerId,
                        principalTable: "HostedRunners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerStorageObservations_HostedRunnerId_MeasuredAtUtc",
                table: "HostedRunnerStorageObservations",
                columns: new[] { "HostedRunnerId", "MeasuredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerStorageVerifications_HostedRunnerId_Status",
                table: "HostedRunnerStorageVerifications",
                columns: new[] { "HostedRunnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerStorageVerifications_IdempotencyKey",
                table: "HostedRunnerStorageVerifications",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedRunnerStorageObservations");

            migrationBuilder.DropTable(
                name: "HostedRunnerStorageVerifications");

            migrationBuilder.DropColumn(
                name: "StorageAvailableBytes",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageMeasuredAtUtc",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageMeasurementError",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageMeasurementStatus",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageReceivedAtUtc",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageReserveRequiredBytes",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageState",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageTotalBytes",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageUsedBytes",
                table: "RunnerHosts");

            migrationBuilder.DropColumn(
                name: "StorageAllowanceBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageMeasuredAtUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageMeasurementError",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageMeasurementSource",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageMeasurementStatus",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageReceivedAtUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageSelfReportedAtUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageSelfReportedError",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageSelfReportedStatus",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageSelfReportedSystemBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageSelfReportedTotalBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageSelfReportedUserBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageState",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageSystemBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageTotalBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageUsagePercent",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageUserBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageVerificationRequestedAtUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageVerifiedAtUtc",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageVerifiedSystemBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageVerifiedTotalBytes",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "StorageVerifiedUserBytes",
                table: "HostedRunners");
        }
    }
}
