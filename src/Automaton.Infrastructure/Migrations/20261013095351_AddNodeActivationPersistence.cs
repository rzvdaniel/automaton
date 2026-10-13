using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeActivationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NodeHostFenceReceiptUses",
                columns: table => new
                {
                    ReceiptId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    NodeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ActivationRequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    HostFenceGeneration = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeHostFenceReceiptUses", x => x.ReceiptId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "NodeManagement",
                columns: table => new
                {
                    NodeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InstallationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EnrollmentAuthorizationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ControlPlaneRealmId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CredentialRevision = table.Column<long>(type: "bigint", nullable: false),
                    RecoveryEpoch = table.Column<long>(type: "bigint", nullable: false),
                    CurrentIncarnation = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentActivationRequestId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    HostFenceGeneration = table.Column<long>(type: "bigint", nullable: false),
                    RecoveryCheckpointSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeManagement", x => x.NodeId);
                    table.CheckConstraint("CK_NodeManagement_PositiveFenceState", "CredentialRevision > 0 AND RecoveryEpoch > 0 AND (CurrentIncarnation IS NULL OR CurrentIncarnation > 0) AND HostFenceGeneration >= 0");
                    table.ForeignKey(
                        name: "FK_NodeManagement_NodeEnrollmentAuthorizations_EnrollmentAuthor~",
                        column: x => x.EnrollmentAuthorizationId,
                        principalTable: "NodeEnrollmentAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "NodeActivationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    NodeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InstallationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ActivationRequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RequestSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedIncarnation = table.Column<long>(type: "bigint", nullable: true),
                    AssignedIncarnation = table.Column<long>(type: "bigint", nullable: false),
                    ControlPlaneRealmId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RecoveryEpoch = table.Column<long>(type: "bigint", nullable: false),
                    CredentialRevision = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImmutableReceiptJson = table.Column<byte[]>(type: "longblob", nullable: false),
                    HostFenceReceiptId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeActivationAttempts", x => x.Id);
                    table.CheckConstraint("CK_NodeActivationAttempts_PositiveState", "RecoveryEpoch > 0 AND CredentialRevision > 0 AND AssignedIncarnation > 0 AND (ExpectedIncarnation IS NULL OR ExpectedIncarnation > 0)");
                    table.ForeignKey(
                        name: "FK_NodeActivationAttempts_NodeManagement_NodeId",
                        column: x => x.NodeId,
                        principalTable: "NodeManagement",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_NodeActivationAttempts_HostFenceReceiptId",
                table: "NodeActivationAttempts",
                column: "HostFenceReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeActivationAttempts_NodeId_ActivationRequestId",
                table: "NodeActivationAttempts",
                columns: new[] { "NodeId", "ActivationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeHostFenceReceiptUses_NodeId_ActivationRequestId",
                table: "NodeHostFenceReceiptUses",
                columns: new[] { "NodeId", "ActivationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeHostFenceReceiptUses_NodeId_HostFenceGeneration",
                table: "NodeHostFenceReceiptUses",
                columns: new[] { "NodeId", "HostFenceGeneration" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeManagement_EnrollmentAuthorizationId",
                table: "NodeManagement",
                column: "EnrollmentAuthorizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeManagement_InstallationId",
                table: "NodeManagement",
                column: "InstallationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NodeActivationAttempts");

            migrationBuilder.DropTable(
                name: "NodeHostFenceReceiptUses");

            migrationBuilder.DropTable(
                name: "NodeManagement");
        }
    }
}
