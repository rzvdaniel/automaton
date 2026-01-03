using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserGitHubTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    EncryptedToken = table.Column<string>(type: "longtext", nullable: false),
                    GitHubUsername = table.Column<string>(type: "varchar(255)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastValidatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IsValid = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGitHubTokens", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UserGitHubTokens_UserId",
                table: "UserGitHubTokens",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserGitHubTokens");
        }
    }
}
