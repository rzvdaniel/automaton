using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAiApiKeyUsersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the AiApiKeyUsers table
            migrationBuilder.DropTable(
                name: "AiApiKeyUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate the AiApiKeyUsers table if rolling back
            migrationBuilder.CreateTable(
                name: "AiApiKeyUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AiApiKeyId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiApiKeyUsers", x => new { x.AiApiKeyId, x.UserId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeyUser_AiApiKeyId",
                table: "AiApiKeyUsers",
                column: "AiApiKeyId");
        }
    }
}
