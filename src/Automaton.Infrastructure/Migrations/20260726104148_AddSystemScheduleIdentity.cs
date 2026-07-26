using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemScheduleIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SystemIdentity",
                table: "ActionSchedules",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionSchedules_SystemIdentity",
                table: "ActionSchedules",
                column: "SystemIdentity",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActionSchedules_SystemIdentity",
                table: "ActionSchedules");

            migrationBuilder.DropColumn(
                name: "SystemIdentity",
                table: "ActionSchedules");
        }
    }
}
