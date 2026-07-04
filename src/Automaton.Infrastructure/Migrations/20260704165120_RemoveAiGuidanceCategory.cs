using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAiGuidanceCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiGuidanceDocuments_Surface_Category",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AiGuidanceDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_Surface",
                table: "AiGuidanceDocuments",
                column: "Surface");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiGuidanceDocuments_Surface",
                table: "AiGuidanceDocuments");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "AiGuidanceDocuments",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_Surface_Category",
                table: "AiGuidanceDocuments",
                columns: new[] { "Surface", "Category" });
        }
    }
}
