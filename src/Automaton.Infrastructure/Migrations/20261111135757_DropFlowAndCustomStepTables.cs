using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropFlowAndCustomStepTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Chat sessions for the removed Custom Step (1) and Flow (3) resource types become general chats.
            migrationBuilder.Sql("UPDATE \"AiChatSessions\" SET \"ResourceType\" = 0 WHERE \"ResourceType\" IN (1, 3);");

            migrationBuilder.DropTable(
                name: "AppVersionSteps");

            migrationBuilder.DropTable(
                name: "CustomStepUsers");

            migrationBuilder.DropTable(
                name: "FlowExecutionLogs");

            migrationBuilder.DropTable(
                name: "FlowExecutionUsers");

            migrationBuilder.DropTable(
                name: "FlowUsers");

            migrationBuilder.DropTable(
                name: "ProjectCustomSteps");

            migrationBuilder.DropTable(
                name: "ProjectFlows");

            migrationBuilder.DropTable(
                name: "ScheduleUsers");

            migrationBuilder.DropTable(
                name: "SensitiveVariableUsers");

            migrationBuilder.DropTable(
                name: "WebAppSteps");

            migrationBuilder.DropTable(
                name: "FlowExecutions");

            migrationBuilder.DropTable(
                name: "Flows");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropTable(
                name: "SensitiveVariables");

            migrationBuilder.DropTable(
                name: "CustomSteps");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "citext", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "citext", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    MoreInfo = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "citext", nullable: false),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VisibleInExplorer = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomSteps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FlowExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Application = table.Column<string>(type: "text", nullable: true),
                    Finished = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Name = table.Column<string>(type: "citext", nullable: true),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CronRecurrence = table.Column<string>(type: "text", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", nullable: false),
                    RunnerIds = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SensitiveVariables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    EncryptedValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VariableScope = table.Column<string>(type: "citext", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensitiveVariables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppVersionSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    CodeSnapshot = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true),
                    StepName = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    VariablesSnapshot = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppVersionSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppVersionSteps_AppVersions_AppVersionId",
                        column: x => x.AppVersionId,
                        principalTable: "AppVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppVersionSteps_CustomSteps_StepId",
                        column: x => x.StepId,
                        principalTable: "CustomSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomStepUsers",
                columns: table => new
                {
                    CustomStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomStepUsers", x => new { x.CustomStepId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CustomStepUsers_CustomSteps_CustomStepId",
                        column: x => x.CustomStepId,
                        principalTable: "CustomSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCustomSteps",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCustomSteps", x => new { x.ProjectId, x.CustomStepId });
                    table.ForeignKey(
                        name: "FK_ProjectCustomSteps_AspNetUsers_AddedBy",
                        column: x => x.AddedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectCustomSteps_CustomSteps_CustomStepId",
                        column: x => x.CustomStepId,
                        principalTable: "CustomSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectCustomSteps_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebAppSteps",
                columns: table => new
                {
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAppSteps", x => new { x.WebAppId, x.CustomStepId });
                    table.ForeignKey(
                        name: "FK_WebAppSteps_AspNetUsers_LinkedBy",
                        column: x => x.LinkedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WebAppSteps_CustomSteps_CustomStepId",
                        column: x => x.CustomStepId,
                        principalTable: "CustomSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WebAppSteps_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlowExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LogsJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowExecutionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlowExecutionLogs_FlowExecutions_FlowExecutionId",
                        column: x => x.FlowExecutionId,
                        principalTable: "FlowExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlowExecutionUsers",
                columns: table => new
                {
                    FlowExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowExecutionUsers", x => new { x.FlowExecutionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_FlowExecutionUsers_FlowExecutions_FlowExecutionId",
                        column: x => x.FlowExecutionId,
                        principalTable: "FlowExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlowUsers",
                columns: table => new
                {
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowUsers", x => new { x.FlowId, x.UserId });
                    table.ForeignKey(
                        name: "FK_FlowUsers_Flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectFlows",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFlows", x => new { x.ProjectId, x.FlowId });
                    table.ForeignKey(
                        name: "FK_ProjectFlows_AspNetUsers_AddedBy",
                        column: x => x.AddedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectFlows_Flows_FlowId",
                        column: x => x.FlowId,
                        principalTable: "Flows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectFlows_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleUsers",
                columns: table => new
                {
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleUsers", x => new { x.ScheduleId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ScheduleUsers_Schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "Schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SensitiveVariableUsers",
                columns: table => new
                {
                    SensitiveVariableId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensitiveVariableUsers", x => new { x.SensitiveVariableId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SensitiveVariableUsers_SensitiveVariables_SensitiveVariable~",
                        column: x => x.SensitiveVariableId,
                        principalTable: "SensitiveVariables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppVersionSteps_AppVersionId",
                table: "AppVersionSteps",
                column: "AppVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppVersionSteps_StepId",
                table: "AppVersionSteps",
                column: "StepId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomStepUser_CustomStepId",
                table: "CustomStepUsers",
                column: "CustomStepId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowExecutionLogs_FlowExecutionId",
                table: "FlowExecutionLogs",
                column: "FlowExecutionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlowExecutionUser_FlowExecutionId",
                table: "FlowExecutionUsers",
                column: "FlowExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowUser_FlowId",
                table: "FlowUsers",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCustomSteps_AddedBy",
                table: "ProjectCustomSteps",
                column: "AddedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCustomSteps_CustomStepId",
                table: "ProjectCustomSteps",
                column: "CustomStepId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCustomSteps_ProjectId",
                table: "ProjectCustomSteps",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFlows_AddedBy",
                table: "ProjectFlows",
                column: "AddedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFlows_FlowId",
                table: "ProjectFlows",
                column: "FlowId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFlows_ProjectId",
                table: "ProjectFlows",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleUser_ScheduleId",
                table: "ScheduleUsers",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_SensitiveVariables_IsActive",
                table: "SensitiveVariables",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SensitiveVariables_LastUsedAt",
                table: "SensitiveVariables",
                column: "LastUsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SensitiveVariables_ResourceType_ResourceId",
                table: "SensitiveVariables",
                columns: new[] { "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SensitiveVariableUser_SensitiveVariableId",
                table: "SensitiveVariableUsers",
                column: "SensitiveVariableId");

            migrationBuilder.CreateIndex(
                name: "IX_WebAppSteps_CustomStepId",
                table: "WebAppSteps",
                column: "CustomStepId");

            migrationBuilder.CreateIndex(
                name: "IX_WebAppSteps_LinkedBy",
                table: "WebAppSteps",
                column: "LinkedBy");

            migrationBuilder.CreateIndex(
                name: "IX_WebAppSteps_WebAppId",
                table: "WebAppSteps",
                column: "WebAppId");
        }
    }
}
