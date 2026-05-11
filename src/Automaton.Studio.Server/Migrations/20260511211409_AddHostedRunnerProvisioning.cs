using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunnerHosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Provider = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Region = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublicIp = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsLocal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalCpuCores = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TotalMemoryMb = table.Column<int>(type: "int", nullable: false),
                    TotalDiskMb = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerHosts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HostedRunners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SubscriptionPlanId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RunnerHostId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RunnerId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ApiKeyId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CpuCores = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    MemoryMb = table.Column<int>(type: "int", nullable: false),
                    DiskMb = table.Column<int>(type: "int", nullable: false),
                    MaxApps = table.Column<int>(type: "int", nullable: false),
                    ContainerName = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContainerId = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastError = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ProvisionedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunners_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HostedRunners_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedRunners_RunnerHosts_RunnerHostId",
                        column: x => x.RunnerHostId,
                        principalTable: "RunnerHosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HostedRunners_Runners_RunnerId",
                        column: x => x.RunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HostedRunners_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HostedRunnerDeployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    HostedRunnerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Action = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CommandPreview = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Logs = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Error = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerDeployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerDeployments_HostedRunners_HostedRunnerId",
                        column: x => x.HostedRunnerId,
                        principalTable: "HostedRunners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_HostedRunnerId",
                table: "HostedRunnerDeployments",
                column: "HostedRunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_Status",
                table: "HostedRunnerDeployments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_ApiKeyId",
                table: "HostedRunners",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_RunnerHostId",
                table: "HostedRunners",
                column: "RunnerHostId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_RunnerId",
                table: "HostedRunners",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_Status",
                table: "HostedRunners",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_SubscriptionPlanId",
                table: "HostedRunners",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_UserId",
                table: "HostedRunners",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_Provider",
                table: "RunnerHosts",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_Status",
                table: "RunnerHosts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostedRunnerDeployments");

            migrationBuilder.DropTable(
                name: "HostedRunners");

            migrationBuilder.DropTable(
                name: "RunnerHosts");
        }
    }
}
