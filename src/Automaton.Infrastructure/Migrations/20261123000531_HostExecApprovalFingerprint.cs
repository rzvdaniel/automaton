using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HostExecApprovalFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovedFor",
                table: "AppRunnerPermissions",
                type: "citext",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodeFingerprint",
                table: "AppRunnerPermissions",
                type: "citext",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedFor",
                table: "AppRunnerPermissions");

            migrationBuilder.DropColumn(
                name: "CodeFingerprint",
                table: "AppRunnerPermissions");
        }
    }
}
