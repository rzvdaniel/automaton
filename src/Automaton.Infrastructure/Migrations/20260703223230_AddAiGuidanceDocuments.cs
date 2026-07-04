using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiGuidanceDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiGuidanceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Key = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelativePath = table.Column<string>(type: "varchar(260)", maxLength: 260, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Surface = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TagsJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentMarkdown = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGuidanceDocuments", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_IsEnabled",
                table: "AiGuidanceDocuments",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_Key",
                table: "AiGuidanceDocuments",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_RelativePath",
                table: "AiGuidanceDocuments",
                column: "RelativePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiGuidanceDocuments_Surface_Category",
                table: "AiGuidanceDocuments",
                columns: new[] { "Surface", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiGuidanceDocuments");
        }
    }
}
