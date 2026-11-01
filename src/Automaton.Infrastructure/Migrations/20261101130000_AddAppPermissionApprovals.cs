using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations;

[DbContext(typeof(Automaton.Api.Data.ApplicationDbContext))]
[Migration("20261101130000_AddAppPermissionApprovals")]
public sealed class AddAppPermissionApprovals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppPermissionApprovals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                AppId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                Permission = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending")
                    .Annotation("MySql:CharSet", "utf8mb4"),
                ProtectedKey = table.Column<string>(type: "longtext", nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppPermissionApprovals", x => x.Id);
                table.ForeignKey("FK_AppPermissionApprovals_WebApps_AppId", x => x.AppId,
                    principalTable: "WebApps", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateIndex(name: "IX_AppPermissionApprovals_AppId_Permission", table: "AppPermissionApprovals", columns: new[] { "AppId", "Permission" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_AppPermissionApprovals_TokenHash", table: "AppPermissionApprovals", column: "TokenHash", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AppPermissionApprovals");
    }
}
