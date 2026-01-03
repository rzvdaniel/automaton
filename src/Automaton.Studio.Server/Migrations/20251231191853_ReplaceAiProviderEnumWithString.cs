using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceAiProviderEnumWithString : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AiApiKeys");

            migrationBuilder.AddColumn<Guid>(
                name: "AiProviderId",
                table: "AiApiKeys",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "AiApiKeys",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AiProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BaseUrl = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    RequiresApiKey = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    EnvironmentKeyName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviders", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AiModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AiProviderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ModelId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    MaxTokens = table.Column<int>(type: "int", nullable: false, defaultValue: 4096),
                    SupportsStreaming = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: true),
                    SupportsTools = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    SupportsVision = table.Column<bool>(type: "tinyint(1)", nullable: false, defaultValue: false),
                    CostPer1kTokensInput = table.Column<decimal>(type: "decimal(10,6)", nullable: true),
                    CostPer1kTokensOutput = table.Column<decimal>(type: "decimal(10,6)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiModels_AiProviders_AiProviderId",
                        column: x => x.AiProviderId,
                        principalTable: "AiProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeys_AiProviderId",
                table: "AiApiKeys",
                column: "AiProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeys_ProviderName",
                table: "AiApiKeys",
                column: "ProviderName");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_AiProviderId",
                table: "AiModels",
                column: "AiProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_AiProviderId_ModelId",
                table: "AiModels",
                columns: new[] { "AiProviderId", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_IsActive",
                table: "AiModels",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_IsDefault",
                table: "AiModels",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_IsActive",
                table: "AiProviders",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_Name",
                table: "AiProviders",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_SortOrder",
                table: "AiProviders",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_AiApiKeys_AiProviders_AiProviderId",
                table: "AiApiKeys",
                column: "AiProviderId",
                principalTable: "AiProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiApiKeys_AiProviders_AiProviderId",
                table: "AiApiKeys");

            migrationBuilder.DropTable(
                name: "AiModels");

            migrationBuilder.DropTable(
                name: "AiProviders");

            migrationBuilder.DropIndex(
                name: "IX_AiApiKeys_AiProviderId",
                table: "AiApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_AiApiKeys_ProviderName",
                table: "AiApiKeys");

            migrationBuilder.DropColumn(
                name: "AiProviderId",
                table: "AiApiKeys");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "AiApiKeys");

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "AiApiKeys",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
