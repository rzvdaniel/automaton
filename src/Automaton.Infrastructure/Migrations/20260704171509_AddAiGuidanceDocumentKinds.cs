using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiGuidanceDocumentKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiGuidanceDocuments_Surface",
                table: "AiGuidanceDocuments");

            migrationBuilder.AddColumn<string>(
                name: "DocumentKind",
                table: "AiGuidanceDocuments",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsModelLoadable",
                table: "AiGuidanceDocuments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiredByDefault",
                table: "AiGuidanceDocuments",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "AiGuidanceDocuments",
                type: "int",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_Surface_DocumentKind",
                table: "AiGuidanceDocuments",
                columns: new[] { "Surface", "DocumentKind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiGuidanceDocuments_Surface_DocumentKind",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "DocumentKind",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "IsModelLoadable",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "RequiredByDefault",
                table: "AiGuidanceDocuments");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "AiGuidanceDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_Surface",
                table: "AiGuidanceDocuments",
                column: "Surface");
        }
    }
}
