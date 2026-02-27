using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddComplexityTierToAiChatSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ComplexityTier",
                table: "AiChatSessions",
                type: "longtext",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ComplexityTier",
                table: "AiChatSessions");
        }
    }
}
