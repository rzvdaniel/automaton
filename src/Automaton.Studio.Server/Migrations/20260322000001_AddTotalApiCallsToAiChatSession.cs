using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTotalApiCallsToAiChatSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalApiCalls",
                table: "AiChatSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalApiCalls",
                table: "AiChatSessions");
        }
    }
}
