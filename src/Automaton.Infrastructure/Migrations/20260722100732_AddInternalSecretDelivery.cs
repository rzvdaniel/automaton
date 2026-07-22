using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalSecretDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecretDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretDefinitions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SecretVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SecretDefinitionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ProtectedValue = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecretVersions_SecretDefinitions_SecretDefinitionId",
                        column: x => x.SecretDefinitionId,
                        principalTable: "SecretDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ExecutionSecretGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OperationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SecretVersionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AppId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ActionKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RunnerId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RunnerHostId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    DeliveryType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaximumReads = table.Column<int>(type: "int", nullable: false),
                    ReadCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LeasedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionSecretGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionSecretGrants_SecretVersions_SecretVersionId",
                        column: x => x.SecretVersionId,
                        principalTable: "SecretVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSecretGrants_ExpiresAtUtc_RevokedAtUtc",
                table: "ExecutionSecretGrants",
                columns: new[] { "ExpiresAtUtc", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSecretGrants_OperationId_RevokedAtUtc_ConsumedAtUtc",
                table: "ExecutionSecretGrants",
                columns: new[] { "OperationId", "RevokedAtUtc", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSecretGrants_RunnerId",
                table: "ExecutionSecretGrants",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSecretGrants_SecretVersionId",
                table: "ExecutionSecretGrants",
                column: "SecretVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SecretDefinitions_OwnerType_OwnerId_DeletedAtUtc",
                table: "SecretDefinitions",
                columns: new[] { "OwnerType", "OwnerId", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecretDefinitions_OwnerType_OwnerId_Name",
                table: "SecretDefinitions",
                columns: new[] { "OwnerType", "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretVersions_ExpiresAtUtc_RevokedAtUtc",
                table: "SecretVersions",
                columns: new[] { "ExpiresAtUtc", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecretVersions_SecretDefinitionId_Version",
                table: "SecretVersions",
                columns: new[] { "SecretDefinitionId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionSecretGrants");

            migrationBuilder.DropTable(
                name: "SecretVersions");

            migrationBuilder.DropTable(
                name: "SecretDefinitions");
        }
    }
}
