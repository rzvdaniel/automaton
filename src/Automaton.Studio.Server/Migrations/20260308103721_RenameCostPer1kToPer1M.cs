using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class RenameCostPer1kToPer1M : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CostPer1kTokensOutput",
                table: "AiModels",
                newName: "CostPer1MTokensOutput");

            migrationBuilder.RenameColumn(
                name: "CostPer1kTokensInput",
                table: "AiModels",
                newName: "CostPer1MTokensInput");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CostPer1MTokensOutput",
                table: "AiModels",
                newName: "CostPer1kTokensOutput");

            migrationBuilder.RenameColumn(
                name: "CostPer1MTokensInput",
                table: "AiModels",
                newName: "CostPer1kTokensInput");
        }
    }
}
