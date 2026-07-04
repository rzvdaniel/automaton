using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAiGuidanceRelativePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiGuidanceDocuments_RelativePath",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "RelativePath",
                table: "AiGuidanceDocuments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RelativePath",
                table: "AiGuidanceDocuments",
                type: "varchar(260)",
                maxLength: 260,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_RelativePath",
                table: "AiGuidanceDocuments",
                column: "RelativePath",
                unique: true);
        }
    }
}
