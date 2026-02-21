using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAiChatUsageMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add token columns to AiChatMessage table
            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "AiChatMessages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "AiChatMessages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTokens",
                table: "AiChatMessages",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "AiChatMessages",
                type: "longtext",
                nullable: true);

            // Add usage tracking columns to AiChatSession table
            migrationBuilder.AddColumn<int>(
                name: "TotalPromptsSubmitted",
                table: "AiChatSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalInputTokens",
                table: "AiChatSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalOutputTokens",
                table: "AiChatSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalTokens",
                table: "AiChatSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAt",
                table: "AiChatSessions",
                type: "datetime(6)",
                nullable: true);

            // Add app-level chat usage columns to WebApps table
            migrationBuilder.AddColumn<int>(
                name: "TotalChatTokensUsed",
                table: "WebApps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalPromptsSubmittedInChat",
                table: "WebApps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChatActivityAt",
                table: "WebApps",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove columns from WebApps table
            migrationBuilder.DropColumn(
                name: "TotalChatTokensUsed",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "TotalPromptsSubmittedInChat",
                table: "WebApps");

            migrationBuilder.DropColumn(
                name: "LastChatActivityAt",
                table: "WebApps");

            // Remove columns from AiChatSessions table
            migrationBuilder.DropColumn(
                name: "TotalPromptsSubmitted",
                table: "AiChatSessions");

            migrationBuilder.DropColumn(
                name: "TotalInputTokens",
                table: "AiChatSessions");

            migrationBuilder.DropColumn(
                name: "TotalOutputTokens",
                table: "AiChatSessions");

            migrationBuilder.DropColumn(
                name: "TotalTokens",
                table: "AiChatSessions");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "AiChatSessions");

            // Remove columns from AiChatMessages table
            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "AiChatMessages");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "AiChatMessages");

            migrationBuilder.DropColumn(
                name: "TotalTokens",
                table: "AiChatMessages");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "AiChatMessages");
        }
    }
}
