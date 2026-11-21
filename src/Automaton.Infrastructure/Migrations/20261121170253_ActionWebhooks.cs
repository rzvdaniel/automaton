using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActionWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TriggerName",
                table: "AppModuleExecutions",
                type: "citext",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActionWebhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionKey = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    SecretHash = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCalledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionWebhooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionWebhooks_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActionWebhooks_WebApps_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActionWebhookCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<string>(type: "citext", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionWebhookCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionWebhookCalls_ActionWebhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalTable: "ActionWebhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionWebhookCalls_WebhookId_CalledAtUtc",
                table: "ActionWebhookCalls",
                columns: new[] { "WebhookId", "CalledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionWebhooks_ApplicationId_ActionKey",
                table: "ActionWebhooks",
                columns: new[] { "ApplicationId", "ActionKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionWebhooks_OwnerUserId",
                table: "ActionWebhooks",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionWebhookCalls");

            migrationBuilder.DropTable(
                name: "ActionWebhooks");

            migrationBuilder.DropColumn(
                name: "TriggerName",
                table: "AppModuleExecutions");
        }
    }
}
