using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "ApiKeys",
                type: "int",
                nullable: false,
                defaultValue: 1);

            // Preserve a useful source for credentials that predate this column.
            migrationBuilder.Sql("UPDATE ApiKeys SET Source = 3 WHERE Scope = 3");
            migrationBuilder.Sql("UPDATE ApiKeys SET Source = 4 WHERE Scope = 6");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "ApiKeys");
        }
    }
}
