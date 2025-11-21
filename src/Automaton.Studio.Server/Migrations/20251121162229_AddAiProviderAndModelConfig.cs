using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviderAndModelConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProviderConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProviderName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Provider = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    DefaultModel = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderConfigs", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AiModelConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProviderId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ModelName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiModelConfigs_AiProviderConfigs_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "AiProviderConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConfigs_Provider",
                table: "AiProviderConfigs",
                column: "Provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConfigs_Enabled",
                table: "AiProviderConfigs",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConfigs_DisplayOrder",
                table: "AiProviderConfigs",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_AiModelConfigs_ProviderId",
                table: "AiModelConfigs",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModelConfigs_ProviderId_ModelName",
                table: "AiModelConfigs",
                columns: new[] { "ProviderId", "ModelName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiModelConfigs_Enabled",
                table: "AiModelConfigs",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_AiModelConfigs_DisplayOrder",
                table: "AiModelConfigs",
                column: "DisplayOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiModelConfigs");

            migrationBuilder.DropTable(
                name: "AiProviderConfigs");
        }
    }
}
