using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeEnrollmentAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NodeEnrollmentAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    NodeId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    InstallationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ControlPlaneRealmId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerScope = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PinnedPublicKeySha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EnrollmentTokenSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CommittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CertificateDer = table.Column<byte[]>(type: "longblob", nullable: true),
                    CertificateSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NodeApiKeySha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProtectedCredentialBundle = table.Column<byte[]>(type: "longblob", nullable: true),
                    CredentialBundleExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeEnrollmentAuthorizations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "NodeEnrollmentRecoveryChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EnrollmentAuthorizationId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Nonce = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    PublicKeySha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeEnrollmentRecoveryChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeEnrollmentRecoveryChallenges_NodeEnrollmentAuthorization~",
                        column: x => x.EnrollmentAuthorizationId,
                        principalTable: "NodeEnrollmentAuthorizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_NodeEnrollmentAuthorizations_EnrollmentTokenSha256",
                table: "NodeEnrollmentAuthorizations",
                column: "EnrollmentTokenSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeEnrollmentAuthorizations_Id_Status",
                table: "NodeEnrollmentAuthorizations",
                columns: new[] { "Id", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeEnrollmentAuthorizations_InstallationId",
                table: "NodeEnrollmentAuthorizations",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeEnrollmentAuthorizations_NodeId",
                table: "NodeEnrollmentAuthorizations",
                column: "NodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeEnrollmentRecoveryChallenges_EnrollmentAuthorizationId_E~",
                table: "NodeEnrollmentRecoveryChallenges",
                columns: new[] { "EnrollmentAuthorizationId", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NodeEnrollmentRecoveryChallenges");

            migrationBuilder.DropTable(
                name: "NodeEnrollmentAuthorizations");
        }
    }
}
