using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropAiGuidanceDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiGuidanceDocuments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiGuidanceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    DocumentKind = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsModelLoadable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsSeedRetired = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Key = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    LastSeedSynchronizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OutputPath = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    RequiredByDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SeedContentHash = table.Column<string>(type: "citext", maxLength: 64, nullable: true),
                    SeedVersion = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    Source = table.Column<string>(type: "citext", maxLength: 20, nullable: false, defaultValue: "Admin"),
                    Surface = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    TagsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Title = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGuidanceDocuments", x => x.Id);
                });

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
                name: "IX_AiGuidanceDocuments_Surface_DocumentKind",
                table: "AiGuidanceDocuments",
                columns: new[] { "Surface", "DocumentKind" });
        }
    }
}
