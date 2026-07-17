using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostedRunnerSpecifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HostedRunners_SubscriptionPlans_SubscriptionPlanId",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "CpuCores",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "DiskMb",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "MaxApps",
                table: "HostedRunners");

            migrationBuilder.RenameColumn(
                name: "SubscriptionPlanId",
                table: "HostedRunners",
                newName: "HostedRunnerSpecificationId");

            migrationBuilder.RenameColumn(
                name: "MemoryMb",
                table: "HostedRunners",
                newName: "SpecificationRevision");

            migrationBuilder.RenameIndex(
                name: "IX_HostedRunners_SubscriptionPlanId",
                table: "HostedRunners",
                newName: "IX_HostedRunners_HostedRunnerSpecificationId");

            migrationBuilder.AddColumn<string>(
                name: "PendingSpecificationSnapshot",
                table: "HostedRunners",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SpecificationSnapshot",
                table: "HostedRunners",
                type: "json",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HostedRunnerSpecifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    Resources = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerSpecifications", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HostedRunnerPlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    DefaultSpecificationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ProvisioningEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerPlatformSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerPlatformSettings_HostedRunnerSpecifications_Defa~",
                        column: x => x.DefaultSpecificationId,
                        principalTable: "HostedRunnerSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HostedRunnerSpecificationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SpecificationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    Resources = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerSpecificationRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerSpecificationRevisions_HostedRunnerSpecification~",
                        column: x => x.SpecificationId,
                        principalTable: "HostedRunnerSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerPlatformSettings_DefaultSpecificationId",
                table: "HostedRunnerPlatformSettings",
                column: "DefaultSpecificationId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerSpecificationRevisions_SpecificationId_Revision",
                table: "HostedRunnerSpecificationRevisions",
                columns: new[] { "SpecificationId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerSpecifications_Code",
                table: "HostedRunnerSpecifications",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_HostedRunners_HostedRunnerSpecifications_HostedRunnerSpecifi~",
                table: "HostedRunners",
                column: "HostedRunnerSpecificationId",
                principalTable: "HostedRunnerSpecifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HostedRunners_HostedRunnerSpecifications_HostedRunnerSpecifi~",
                table: "HostedRunners");

            migrationBuilder.DropTable(
                name: "HostedRunnerPlatformSettings");

            migrationBuilder.DropTable(
                name: "HostedRunnerSpecificationRevisions");

            migrationBuilder.DropTable(
                name: "HostedRunnerSpecifications");

            migrationBuilder.DropColumn(
                name: "PendingSpecificationSnapshot",
                table: "HostedRunners");

            migrationBuilder.DropColumn(
                name: "SpecificationSnapshot",
                table: "HostedRunners");

            migrationBuilder.RenameColumn(
                name: "SpecificationRevision",
                table: "HostedRunners",
                newName: "MemoryMb");

            migrationBuilder.RenameColumn(
                name: "HostedRunnerSpecificationId",
                table: "HostedRunners",
                newName: "SubscriptionPlanId");

            migrationBuilder.RenameIndex(
                name: "IX_HostedRunners_HostedRunnerSpecificationId",
                table: "HostedRunners",
                newName: "IX_HostedRunners_SubscriptionPlanId");

            migrationBuilder.AddColumn<decimal>(
                name: "CpuCores",
                table: "HostedRunners",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DiskMb",
                table: "HostedRunners",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxApps",
                table: "HostedRunners",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "FK_HostedRunners_SubscriptionPlans_SubscriptionPlanId",
                table: "HostedRunners",
                column: "SubscriptionPlanId",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
