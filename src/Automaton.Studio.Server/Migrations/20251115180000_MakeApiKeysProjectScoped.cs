using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Studio.Server.Migrations
{
    /// <inheritdoc />
    public partial class MakeApiKeysProjectScoped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop existing foreign key constraints and indexes from ApiKeys
            migrationBuilder.DropForeignKey(
                name: "FK_ApiKeys_AspNetUsers_UserId",
                table: "ApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_ApiKeys_UserId_IsActive",
                table: "ApiKeys");

            // Drop existing foreign key constraints and indexes from AiApiKeys
            migrationBuilder.DropForeignKey(
                name: "FK_AiApiKeys_AspNetUsers_UserId",
                table: "AiApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_AiApiKeys_UserId",
                table: "AiApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_AiApiKeys_UserId_IsActive",
                table: "AiApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_AiApiKeys_UserId_Provider",
                table: "AiApiKeys");

            // Drop UserId columns from ApiKeys and AiApiKeys
            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AiApiKeys");

            // Create ApiKeyUsers table for personal ownership
            migrationBuilder.CreateTable(
                name: "ApiKeyUsers",
                columns: table => new
                {
                    ApiKeyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyUsers", x => new { x.ApiKeyId, x.UserId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyUser_ApiKeyId",
                table: "ApiKeyUsers",
                column: "ApiKeyId");

            // Create ProjectApiKeys table for project association
            migrationBuilder.CreateTable(
                name: "ProjectApiKeys",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AddedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectApiKeys", x => new { x.ProjectId, x.ApiKeyId });
                    table.ForeignKey(
                        name: "FK_ProjectApiKeys_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectApiKeys_AspNetUsers_AddedBy",
                        column: x => x.AddedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectApiKeys_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApiKeys_ApiKeyId",
                table: "ProjectApiKeys",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApiKeys_ProjectId",
                table: "ProjectApiKeys",
                column: "ProjectId");

            // Create AiApiKeyUsers table for personal ownership
            migrationBuilder.CreateTable(
                name: "AiApiKeyUsers",
                columns: table => new
                {
                    AiApiKeyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiApiKeyUsers", x => new { x.AiApiKeyId, x.UserId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeyUser_AiApiKeyId",
                table: "AiApiKeyUsers",
                column: "AiApiKeyId");

            // Create ProjectAiApiKeys table for project association
            migrationBuilder.CreateTable(
                name: "ProjectAiApiKeys",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AiApiKeyId = table.Column<Guid>(type: "char(36)", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AddedBy = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAiApiKeys", x => new { x.ProjectId, x.AiApiKeyId });
                    table.ForeignKey(
                        name: "FK_ProjectAiApiKeys_AiApiKeys_AiApiKeyId",
                        column: x => x.AiApiKeyId,
                        principalTable: "AiApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectAiApiKeys_AspNetUsers_AddedBy",
                        column: x => x.AddedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectAiApiKeys_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAiApiKeys_AiApiKeyId",
                table: "ProjectAiApiKeys",
                column: "AiApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAiApiKeys_ProjectId",
                table: "ProjectAiApiKeys",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the new tables
            migrationBuilder.DropTable(
                name: "ProjectAiApiKeys");

            migrationBuilder.DropTable(
                name: "AiApiKeyUsers");

            migrationBuilder.DropTable(
                name: "ProjectApiKeys");

            migrationBuilder.DropTable(
                name: "ApiKeyUsers");

            // Add UserId columns back
            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ApiKeys",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "AiApiKeys",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Recreate foreign keys and indexes for ApiKeys
            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId_IsActive",
                table: "ApiKeys",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApiKeys_AspNetUsers_UserId",
                table: "ApiKeys",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Recreate foreign keys and indexes for AiApiKeys
            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeys_UserId",
                table: "AiApiKeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeys_UserId_IsActive",
                table: "AiApiKeys",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeys_UserId_Provider",
                table: "AiApiKeys",
                columns: new[] { "UserId", "Provider" });

            migrationBuilder.AddForeignKey(
                name: "FK_AiApiKeys_AspNetUsers_UserId",
                table: "AiApiKeys",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
