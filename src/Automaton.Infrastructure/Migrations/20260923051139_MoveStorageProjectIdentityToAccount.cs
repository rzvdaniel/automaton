using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveStorageProjectIdentityToAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddHistoricalStorageProjectGuard(migrationBuilder, accountScope: true);

            migrationBuilder.CreateTable(
                name: "AccountStorageAllocations",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StorageProjectId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountStorageAllocations", x => x.AccountId);
                    table.CheckConstraint("CK_AccountStorageAllocations_StorageProjectId_Positive", "StorageProjectId > 0");
                    table.ForeignKey(
                        name: "FK_AccountStorageAllocations_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("""
                INSERT INTO AccountStorageAllocations (AccountId, StorageProjectId)
                SELECT UserId, MIN(StorageProjectId)
                FROM HostedRunners
                WHERE StorageProjectId IS NOT NULL
                GROUP BY UserId;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AccountStorageAllocations_StorageProjectId",
                table: "AccountStorageAllocations",
                column: "StorageProjectId",
                unique: true);

            // Keep the old uniqueness guard until its account-level equivalent
            // is present and historical identities have been adopted.
            migrationBuilder.DropIndex(
                name: "IX_HostedRunners_StorageProjectId",
                table: "HostedRunners");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_StorageProjectId",
                table: "HostedRunners",
                column: "StorageProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            AddHistoricalStorageProjectGuard(migrationBuilder, accountScope: false);

            migrationBuilder.DropTable(
                name: "AccountStorageAllocations");

            migrationBuilder.DropIndex(
                name: "IX_HostedRunners_StorageProjectId",
                table: "HostedRunners");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_StorageProjectId",
                table: "HostedRunners",
                column: "StorageProjectId",
                unique: true);
        }

        private void AddHistoricalStorageProjectGuard(MigrationBuilder migrationBuilder, bool accountScope)
        {
            if (ActiveProvider.Contains("SqlServer", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(accountScope
                    ? "CREATE TABLE #StorageProjectMigrationGuard (AccountId uniqueidentifier NOT NULL PRIMARY KEY, StorageProjectId int NOT NULL CHECK (StorageProjectId > 0))"
                    : "CREATE TABLE #StorageProjectMigrationGuard (StorageProjectId int NOT NULL PRIMARY KEY CHECK (StorageProjectId > 0))");
                migrationBuilder.Sql(accountScope
                    ? "INSERT INTO #StorageProjectMigrationGuard (AccountId, StorageProjectId) SELECT UserId, CASE WHEN COUNT(DISTINCT StorageProjectId) = 1 AND MIN(StorageProjectId) > 0 THEN MIN(StorageProjectId) ELSE 0 END FROM HostedRunners WHERE StorageProjectId IS NOT NULL GROUP BY UserId"
                    : "INSERT INTO #StorageProjectMigrationGuard (StorageProjectId) SELECT CASE WHEN COUNT(*) = 1 THEN StorageProjectId ELSE 0 END FROM HostedRunners WHERE StorageProjectId IS NOT NULL GROUP BY StorageProjectId");
                migrationBuilder.Sql("DROP TABLE #StorageProjectMigrationGuard");
                return;
            }

            if (ActiveProvider.Contains("Sqlite", StringComparison.Ordinal))
            {
                migrationBuilder.Sql(accountScope
                    ? "CREATE TEMP TABLE StorageProjectMigrationGuard (AccountId TEXT NOT NULL PRIMARY KEY, StorageProjectId INTEGER NOT NULL CHECK (StorageProjectId > 0))"
                    : "CREATE TEMP TABLE StorageProjectMigrationGuard (StorageProjectId INTEGER NOT NULL PRIMARY KEY CHECK (StorageProjectId > 0))");
                migrationBuilder.Sql(accountScope
                    ? "INSERT INTO StorageProjectMigrationGuard (AccountId, StorageProjectId) SELECT UserId, CASE WHEN COUNT(DISTINCT StorageProjectId) = 1 AND MIN(StorageProjectId) > 0 THEN MIN(StorageProjectId) ELSE 0 END FROM HostedRunners WHERE StorageProjectId IS NOT NULL GROUP BY UserId"
                    : "INSERT INTO StorageProjectMigrationGuard (StorageProjectId) SELECT CASE WHEN COUNT(*) = 1 THEN StorageProjectId ELSE 0 END FROM HostedRunners WHERE StorageProjectId IS NOT NULL GROUP BY StorageProjectId");
                migrationBuilder.Sql("DROP TABLE StorageProjectMigrationGuard");
                return;
            }

            migrationBuilder.Sql(accountScope
                ? "CREATE TEMPORARY TABLE StorageProjectMigrationGuard (AccountId char(36) NOT NULL PRIMARY KEY, StorageProjectId int NOT NULL CHECK (StorageProjectId > 0)) ENGINE=MEMORY"
                : "CREATE TEMPORARY TABLE StorageProjectMigrationGuard (StorageProjectId int NOT NULL PRIMARY KEY CHECK (StorageProjectId > 0)) ENGINE=MEMORY");
            migrationBuilder.Sql(accountScope
                ? "INSERT INTO StorageProjectMigrationGuard (AccountId, StorageProjectId) SELECT UserId, CASE WHEN COUNT(DISTINCT StorageProjectId) = 1 AND MIN(StorageProjectId) > 0 THEN MIN(StorageProjectId) ELSE 0 END FROM HostedRunners WHERE StorageProjectId IS NOT NULL GROUP BY UserId"
                : "INSERT INTO StorageProjectMigrationGuard (StorageProjectId) SELECT CASE WHEN COUNT(*) = 1 THEN StorageProjectId ELSE 0 END FROM HostedRunners WHERE StorageProjectId IS NOT NULL GROUP BY StorageProjectId");
            migrationBuilder.Sql("DROP TEMPORARY TABLE StorageProjectMigrationGuard");
        }
    }
}
