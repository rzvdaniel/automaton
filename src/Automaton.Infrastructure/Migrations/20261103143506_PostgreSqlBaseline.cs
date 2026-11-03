using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Automaton.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PostgreSqlBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "AiChatSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<int>(type: "integer", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "citext", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    TotalPromptsSubmitted = table.Column<int>(type: "integer", nullable: false),
                    TotalInputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalApiCalls = table.Column<int>(type: "integer", nullable: false),
                    ComplexityTier = table.Column<string>(type: "text", nullable: true),
                    TotalCacheReadTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalCacheCreationTokens = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiChatSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiGuidanceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    Surface = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    DocumentKind = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    TagsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    OutputPath = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    ContentMarkdown = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RequiredByDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsModelLoadable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Source = table.Column<string>(type: "citext", maxLength: 20, nullable: false, defaultValue: "Admin"),
                    SeedVersion = table.Column<int>(type: "integer", nullable: true),
                    SeedContentHash = table.Column<string>(type: "citext", maxLength: 64, nullable: true),
                    LastSeedSynchronizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsSeedRetired = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGuidanceDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    BaseUrl = table.Column<string>(type: "citext", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    RequiresApiKey = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    EnvironmentKeyName = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    ApiKeyHelpUrl = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    IconName = table.Column<string>(type: "citext", maxLength: 50, nullable: true),
                    ExtraBodyJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    HashedKey = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    KeyPrefix = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UseCount = table.Column<int>(type: "integer", nullable: false),
                    MaximumUses = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Scope = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    Source = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "citext", maxLength: 80, nullable: false),
                    EventType = table.Column<string>(type: "citext", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "citext", maxLength: 80, nullable: false),
                    Action = table.Column<string>(type: "citext", maxLength: 80, nullable: false),
                    EntityType = table.Column<string>(type: "citext", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    Outcome = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "citext", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "citext", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CliAuthorizationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserCode = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    HashedVerifier = table.Column<string>(type: "citext", maxLength: 128, nullable: false),
                    DeviceName = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    RemoteIp = table.Column<string>(type: "citext", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EncryptedApiKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CredentialIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CliAuthorizationRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    MoreInfo = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    VisibleInExplorer = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Started = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Finished = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Application = table.Column<string>(type: "text", nullable: true)
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
                    Name = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostContainerExecutionOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostedRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerHostId = table.Column<Guid>(type: "uuid", nullable: false),
                    InfrastructureRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeploymentTargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeploymentAttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    RequestHash = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostContainerExecutionOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HostedRunnerSpecifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "citext", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Resources = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerSpecifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfrastructureRunnerUpdateOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerHostId = table.Column<Guid>(type: "uuid", nullable: false),
                    InfrastructureRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    RequestedImage = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    TargetImageDigest = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    PreviousImageDigest = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    PreviousSpecificationVersion = table.Column<int>(type: "integer", nullable: true),
                    TargetSpecificationVersion = table.Column<int>(type: "integer", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReconnectDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Diagnostics = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfrastructureRunnerUpdateOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfrastructureWorkflowExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowVersionId = table.Column<string>(type: "citext", maxLength: 80, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRunnerHostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfrastructureWorkflowExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvitationTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "citext", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "citext", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsConsumed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvitationTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketplaceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "citext", maxLength: 140, nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketplaceCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Expires = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunnerExecutionOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedRequest = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    CancellationRequested = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerExecutionOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerIds = table.Column<string>(type: "text", nullable: false),
                    CronRecurrence = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 200, nullable: true),
                    CurrentVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SelfHostedRunnerEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerName = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    ApplicationProfile = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    EnrollmentTokenHash = table.Column<string>(type: "citext", maxLength: 128, nullable: false),
                    InstallationId = table.Column<string>(type: "citext", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfHostedRunnerEnrollments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SensitiveVariables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    EncryptedValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    ResourceType = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VariableScope = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensitiveVariables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "citext", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    PriceUsd = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    StripeProductId = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    StripePriceId = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CapabilitiesJson = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserGitHubTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedToken = table.Column<string>(type: "text", nullable: false),
                    GitHubUsername = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastValidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGitHubTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingKey = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    SettingValue = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebApps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeletionClaimId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Definition = table.Column<string>(type: "text", nullable: false),
                    ApplicationContractJson = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsForkable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PublicUrl = table.Column<string>(type: "citext", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    RuntimeVisibility = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "private"),
                    CatalogVisibility = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "unlisted"),
                    MarketplacePreviewMode = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "launchable"),
                    RequiresAuthentication = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedRoles = table.Column<string>(type: "text", nullable: true),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ViewCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    StarCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastViewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsSample = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsProfileFeatured = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ThumbnailUrl = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<string>(type: "citext", nullable: true),
                    Tags = table.Column<string>(type: "text", nullable: true),
                    MarketplaceStatus = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "approved"),
                    ReviewStatus = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "approved"),
                    AccessStatus = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "active"),
                    MarketplaceModerationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    MarketplaceModeratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MarketplaceModeratedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ForkedFromAppId = table.Column<Guid>(type: "uuid", nullable: true),
                    ForkedFromAuthorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ForkedFromAppName = table.Column<string>(type: "citext", maxLength: 200, nullable: true),
                    ForkedFromAuthorName = table.Column<string>(type: "citext", maxLength: 200, nullable: true),
                    TotalChatTokensUsed = table.Column<int>(type: "integer", nullable: false),
                    TotalPromptsSubmittedInChat = table.Column<int>(type: "integer", nullable: false),
                    LastChatActivityAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebApps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebAppStars",
                columns: table => new
                {
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAppStars", x => new { x.WebAppId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "AiChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    MessageType = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    TotalTokens = table.Column<int>(type: "integer", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiChatMessages_AiChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AiChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    ProviderName = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    AiProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncryptedApiKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Description = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseUrl = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    DefaultModel = table.Column<string>(type: "citext", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiApiKeys_AiProviders_AiProviderId",
                        column: x => x.AiProviderId,
                        principalTable: "AiProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AiModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AiProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "citext", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsPlanningModel = table.Column<bool>(type: "boolean", nullable: false),
                    MaxTokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 4096),
                    SupportsStreaming = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    SupportsTools = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    SupportsVision = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CostPer1MTokensInput = table.Column<decimal>(type: "numeric(10,6)", nullable: true),
                    CostPer1MTokensOutput = table.Column<decimal>(type: "numeric(10,6)", nullable: true),
                    ExtraBodyJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "ApiKeyUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeyUsers", x => new { x.ApiKeyId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ApiKeyUsers_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Runners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ConnectionId = table.Column<string>(type: "text", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastDisconnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletionRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    Version = table.Column<string>(type: "citext", maxLength: 50, nullable: true),
                    MachineName = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    IpAddress = table.Column<string>(type: "citext", maxLength: 64, nullable: true),
                    ApiInstanceId = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    ApiColor = table.Column<string>(type: "citext", maxLength: 32, nullable: true),
                    ImageDigest = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    ContainerSpecificationVersion = table.Column<int>(type: "integer", nullable: true),
                    ApiCompatibilityVersion = table.Column<string>(type: "citext", maxLength: 40, nullable: true),
                    OperatingSystem = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    Architecture = table.Column<string>(type: "citext", maxLength: 40, nullable: true),
                    ContainerHealth = table.Column<string>(type: "citext", maxLength: 40, nullable: true),
                    MachineStorageMeasuredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MachineStorageTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    MachineStorageAvailableBytes = table.Column<long>(type: "bigint", nullable: true),
                    MachineStorageMeasurementStatus = table.Column<string>(type: "citext", maxLength: 32, nullable: true),
                    MachineStorageMeasurementError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Purpose = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "Managed"),
                    ApplicationProfile = table.Column<string>(type: "citext", maxLength: 40, nullable: true),
                    CapabilitiesJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    InstallationId = table.Column<string>(type: "citext", maxLength: 128, nullable: true),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScriptEngineSettings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Runners_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomStepUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomStepId = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "FlowExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowExecutionId = table.Column<Guid>(type: "uuid", nullable: false)
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
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "HostedRunnerPlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultSpecificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProvisioningEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerPlatformSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerPlatformSettings_HostedRunnerSpecifications_Def~",
                        column: x => x.DefaultSpecificationId,
                        principalTable: "HostedRunnerSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HostedRunnerSpecificationRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SpecificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    Resources = table.Column<string>(type: "jsonb", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerSpecificationRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerSpecificationRevisions_HostedRunnerSpecificatio~",
                        column: x => x.SpecificationId,
                        principalTable: "HostedRunnerSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InfrastructureWorkflowExecutionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Stream = table.Column<string>(type: "citext", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfrastructureWorkflowExecutionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InfrastructureWorkflowExecutionLogs_InfrastructureWorkflowE~",
                        column: x => x.ExecutionId,
                        principalTable: "InfrastructureWorkflowExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "SecretVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ProtectedValue = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecretVersions_SecretDefinitions_SecretDefinitionId",
                        column: x => x.SecretDefinitionId,
                        principalTable: "SecretDefinitions",
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

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    PublicUsername = table.Column<string>(type: "text", nullable: true),
                    Bio = table.Column<string>(type: "text", nullable: true),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    IsEnterpriseEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    StripeCustomerId = table.Column<string>(type: "text", nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "text", nullable: true),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    CanAccessHostedRunnerTestPlans = table.Column<bool>(type: "boolean", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    RegistrationProvider = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserName = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AppPermissionApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    ProtectedKey = table.Column<string>(type: "text", nullable: true),
                    TokenHash = table.Column<string>(type: "citext", maxLength: 64, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPermissionApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPermissionApprovals_WebApps_AppId",
                        column: x => x.AppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetSlug = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    PackagePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Clean = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutorInstanceId = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    ExecutionAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ResultJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentOperations_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebAppScreenshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImageUrl = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    Caption = table.Column<string>(type: "citext", maxLength: 240, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAppScreenshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebAppScreenshots_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebAppUsers",
                columns: table => new
                {
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAppUsers", x => new { x.WebAppId, x.UserId });
                    table.ForeignKey(
                        name: "FK_WebAppUsers_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppDeploymentTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    ExecutionRunnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultConstraintKey = table.Column<byte>(type: "smallint", nullable: true),
                    GenericRuntimeDeployed = table.Column<bool>(type: "boolean", nullable: false),
                    ActiveDeploymentOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveDeploymentExecutionAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastDeploymentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulDeploymentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CleanupPending = table.Column<bool>(type: "boolean", nullable: false),
                    LastCleanupError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastCleanupAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDeploymentTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppDeploymentTargets_Runners_ExecutionRunnerId",
                        column: x => x.ExecutionRunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppDeploymentTargets_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunnerConnectionSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisconnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisconnectReason = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    ApiInstanceId = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    ApiColor = table.Column<string>(type: "citext", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerConnectionSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerConnectionSessions_Runners_RunnerId",
                        column: x => x.RunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunnerHosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    Provider = table.Column<string>(type: "citext", maxLength: 80, nullable: false),
                    ProviderResourceId = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    ProviderServerType = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    ProviderImageId = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    Region = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    PublicIp = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    PrivateIp = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    ProvisioningTransport = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    RuntimeConnectivityMode = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    HostedRunnerSchedulingEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Status = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    ProvisioningStatus = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "manual"),
                    ProvisioningStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InfrastructureRunnerEnrolledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProvisioningFailureCode = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    ProvisioningFailureMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TotalCpuCores = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    ReservedCpuCores = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    TotalMemoryMb = table.Column<int>(type: "integer", nullable: false),
                    ReservedMemoryMb = table.Column<int>(type: "integer", nullable: false),
                    TotalDiskMb = table.Column<int>(type: "integer", nullable: false),
                    ReservedDiskMb = table.Column<int>(type: "integer", nullable: false),
                    StorageMeasuredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageAvailableBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageUsedBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageReserveRequiredBytes = table.Column<long>(type: "bigint", nullable: true),
                    RootFilesystemTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    RootFilesystemAvailableBytes = table.Column<long>(type: "bigint", nullable: true),
                    DockerFilesystemTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    DockerFilesystemAvailableBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageState = table.Column<string>(type: "citext", maxLength: 32, nullable: false, defaultValue: "Unknown"),
                    StorageMeasurementStatus = table.Column<string>(type: "citext", maxLength: 32, nullable: false, defaultValue: "NeverMeasured"),
                    StorageMeasurementError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    HealthJson = table.Column<string>(type: "jsonb", nullable: true),
                    InfrastructureRunnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerHosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerHosts_Runners_InfrastructureRunnerId",
                        column: x => x.InfrastructureRunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RunnerUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerUsers", x => new { x.RunnerId, x.UserId });
                    table.ForeignKey(
                        name: "FK_RunnerUsers_Runners_RunnerId",
                        column: x => x.RunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExecutionSecretGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecretVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionKey = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerHostId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryType = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    DeliveryPolicy = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "singleDelivery"),
                    TargetName = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    MaximumReads = table.Column<int>(type: "integer", nullable: false),
                    ReadCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionSecretGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionSecretGrants_SecretVersions_SecretVersionId",
                        column: x => x.SecretVersionId,
                        principalTable: "SecretVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccountStorageAllocations",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageProjectId = table.Column<int>(type: "integer", nullable: false),
                    StorageRunnerHostId = table.Column<Guid>(type: "uuid", nullable: true),
                    StorageFilesystemUuid = table.Column<string>(type: "citext", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountStorageAllocations", x => x.AccountId);
                    table.CheckConstraint("CK_AccountStorageAllocations_StorageProjectId_Positive", "\"StorageProjectId\" > 0");
                    table.ForeignKey(
                        name: "FK_AccountStorageAllocations_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActionSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "citext", maxLength: 16, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemIdentity = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    ActionKey = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExecutionScope = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    ScheduleType = table.Column<string>(type: "citext", maxLength: 16, nullable: false),
                    RunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IntervalSeconds = table.Column<int>(type: "integer", nullable: true),
                    CronExpression = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    TimeZone = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastTriggeredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyPolicy = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    MisfirePolicy = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<string>(type: "citext", maxLength: 128, nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionSchedules", x => x.Id);
                    table.CheckConstraint("CK_ActionSchedules_Owner", "(\"OwnerType\" = 'User' AND \"OwnerUserId\" IS NOT NULL) OR (\"OwnerType\" = 'System' AND \"OwnerUserId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_ActionSchedules_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiChatSessionUsers",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiChatSessionUsers", x => new { x.SessionId, x.UserId });
                    table.ForeignKey(
                        name: "FK_AiChatSessionUsers_AiChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AiChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiChatSessionUsers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    Reason = table.Column<string>(type: "citext", maxLength: 160, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AiSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    AiModel = table.Column<string>(type: "citext", maxLength: 200, nullable: true),
                    SnapshotPath = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    FilesChangeSummary = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppVersions_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppVersions_WebApps_AppId",
                        column: x => x.AppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "citext", nullable: false),
                    ProviderKey = table.Column<string>(type: "citext", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "citext", nullable: false),
                    Name = table.Column<string>(type: "citext", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    AllowPublicSignup = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RequireEmailDomain = table.Column<string>(type: "citext", maxLength: 255, nullable: true),
                    LogoUrl = table.Column<string>(type: "citext", maxLength: 500, nullable: true),
                    PrimaryColor = table.Column<string>(type: "citext", maxLength: 7, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Organizations_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecentActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActivityType = table.Column<int>(type: "integer", nullable: false),
                    EntityName = table.Column<string>(type: "citext", maxLength: 255, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecentActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecentActivities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebAppSteps",
                columns: table => new
                {
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedBy = table.Column<Guid>(type: "uuid", nullable: true)
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
                name: "HostedRunners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerHostId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    HostedRunnerSpecificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpecificationRevision = table.Column<int>(type: "integer", nullable: false),
                    SpecificationSnapshot = table.Column<string>(type: "jsonb", nullable: false),
                    PendingSpecificationSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    ContainerName = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    ContainerId = table.Column<string>(type: "citext", maxLength: 160, nullable: true),
                    RuntimeEndpoint = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RuntimePort = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProvisionedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageAllowanceBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageProjectId = table.Column<int>(type: "integer", nullable: true),
                    StorageMeasuredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageMeasurementSource = table.Column<string>(type: "citext", maxLength: 40, nullable: false, defaultValue: "RunnerSelfReported"),
                    StorageMeasurementStatus = table.Column<string>(type: "citext", maxLength: 32, nullable: false, defaultValue: "NeverMeasured"),
                    StorageSystemBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageUserBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageUsagePercent = table.Column<decimal>(type: "numeric(7,2)", nullable: true),
                    StorageState = table.Column<string>(type: "citext", maxLength: 32, nullable: false, defaultValue: "Unknown"),
                    StorageMeasurementError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StorageVerificationRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageVerifiedSystemBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageVerifiedUserBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageVerifiedTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageSelfReportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StorageSelfReportedSystemBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageSelfReportedUserBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageSelfReportedTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    StorageSelfReportedStatus = table.Column<string>(type: "citext", maxLength: 32, nullable: false, defaultValue: "NeverMeasured"),
                    StorageSelfReportedError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EnforcementState = table.Column<string>(type: "citext", maxLength: 32, nullable: false, defaultValue: "None"),
                    OverLimitSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GracePeriodEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuspendedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastEnforcementReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastNotificationAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunners_ApiKeys_ApiKeyId",
                        column: x => x.ApiKeyId,
                        principalTable: "ApiKeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HostedRunners_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostedRunners_HostedRunnerSpecifications_HostedRunnerSpecif~",
                        column: x => x.HostedRunnerSpecificationId,
                        principalTable: "HostedRunnerSpecifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HostedRunners_RunnerHosts_RunnerHostId",
                        column: x => x.RunnerHostId,
                        principalTable: "RunnerHosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HostedRunners_Runners_RunnerId",
                        column: x => x.RunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RunnerHostProvisioningOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerHostId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "citext", maxLength: 120, nullable: false),
                    ProviderResourceId = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloudInitTemplateVersion = table.Column<string>(type: "citext", maxLength: 40, nullable: true),
                    CloudInitGeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextTimeoutAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerHostProvisioningOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerHostProvisioningOperations_RunnerHosts_RunnerHostId",
                        column: x => x.RunnerHostId,
                        principalTable: "RunnerHosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActionScheduleOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TriggeredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    FailureSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionScheduleOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionScheduleOccurrences_ActionSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "ActionSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppVersionSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepName = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CodeSnapshot = table.Column<string>(type: "text", nullable: false),
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
                name: "OrganizationAppAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationAppAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationAppAssignments_AspNetUsers_AssignedBy",
                        column: x => x.AssignedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationAppAssignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationAppAssignments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationAppAssignments_WebApps_AppId",
                        column: x => x.AppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "citext", maxLength: 255, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsConsumed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationInvitations_AspNetUsers_InvitedBy",
                        column: x => x.InvitedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationInvitations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_AspNetUsers_InvitedBy",
                        column: x => x.InvitedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMembers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedRunnerDeployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostedRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    CommandPreview = table.Column<string>(type: "text", nullable: true),
                    Logs = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RegistrationDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedRunnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousConnectionId = table.Column<string>(type: "citext", maxLength: 120, nullable: true),
                    ReconnectAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReconnectDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastRecoveryAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerDeployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerDeployments_HostedRunners_HostedRunnerId",
                        column: x => x.HostedRunnerId,
                        principalTable: "HostedRunners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedRunnerStorageObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostedRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    MeasuredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SystemBytes = table.Column<long>(type: "bigint", nullable: false),
                    UserBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RunnerVersion = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerStorageObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerStorageObservations_HostedRunners_HostedRunnerId",
                        column: x => x.HostedRunnerId,
                        principalTable: "HostedRunners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostedRunnerStorageVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostedRunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "citext", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "citext", maxLength: 80, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostedRunnerStorageVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostedRunnerStorageVerifications_HostedRunners_HostedRunner~",
                        column: x => x.HostedRunnerId,
                        principalTable: "HostedRunners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectAiApiKeys",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "ProjectApiKeys",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "ProjectApps",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebAppId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectApps", x => new { x.ProjectId, x.WebAppId });
                    table.ForeignKey(
                        name: "FK_ProjectApps_AspNetUsers_AddedBy",
                        column: x => x.AddedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectApps_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectApps_WebApps_WebAppId",
                        column: x => x.WebAppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCustomSteps",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true)
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
                name: "ProjectFlows",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FlowId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true)
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
                name: "ProjectMembers",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectMembers", x => new { x.ProjectId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ProjectMembers_AspNetUsers_InvitedBy",
                        column: x => x.InvitedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectMembers_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectRunners",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectRunners", x => new { x.ProjectId, x.RunnerId });
                    table.ForeignKey(
                        name: "FK_ProjectRunners_AspNetUsers_AddedBy",
                        column: x => x.AddedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectRunners_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectRunners_Runners_RunnerId",
                        column: x => x.RunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppModuleExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostedRunnerOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduleOccurrenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeploymentTargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModuleType = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    ModuleKey = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerType = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    DeploymentHash = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    ErrorCode = table.Column<string>(type: "citext", maxLength: 80, nullable: true),
                    FailureSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutionDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LogsAvailableUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultAvailableUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "citext", maxLength: 128, nullable: true),
                    ResultFingerprint = table.Column<string>(type: "citext", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppModuleExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppModuleExecutions_AppDeploymentTargets_DeploymentTargetId",
                        column: x => x.DeploymentTargetId,
                        principalTable: "AppDeploymentTargets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppModuleExecutions_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppModuleExecutions_HostedRunnerDeployments_HostedRunnerOpe~",
                        column: x => x.HostedRunnerOperationId,
                        principalTable: "HostedRunnerDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppModuleExecutions_Runners_RunnerId",
                        column: x => x.RunnerId,
                        principalTable: "Runners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppModuleExecutions_WebApps_AppId",
                        column: x => x.AppId,
                        principalTable: "WebApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunnerEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunnerHostId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostedRunnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SecretVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedPurpose = table.Column<string>(type: "citext", maxLength: 40, nullable: false),
                    ExpectedCapabilitiesJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EnrollmentTokenHash = table.Column<string>(type: "citext", maxLength: 128, nullable: false),
                    InstallationId = table.Column<string>(type: "citext", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "citext", maxLength: 32, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RunnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunnerEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunnerEnrollments_HostedRunnerDeployments_OperationId",
                        column: x => x.OperationId,
                        principalTable: "HostedRunnerDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunnerEnrollments_HostedRunners_HostedRunnerId",
                        column: x => x.HostedRunnerId,
                        principalTable: "HostedRunners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunnerEnrollments_RunnerHosts_RunnerHostId",
                        column: x => x.RunnerHostId,
                        principalTable: "RunnerHosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunnerEnrollments_SecretVersions_SecretVersionId",
                        column: x => x.SecretVersionId,
                        principalTable: "SecretVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountStorageAllocations_StorageProjectId",
                table: "AccountStorageAllocations",
                column: "StorageProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionScheduleOccurrences_IdempotencyKey",
                table: "ActionScheduleOccurrences",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionScheduleOccurrences_ScheduleId_Status_ScheduledForUtc",
                table: "ActionScheduleOccurrences",
                columns: new[] { "ScheduleId", "Status", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionSchedules_Enabled_NextRunAtUtc",
                table: "ActionSchedules",
                columns: new[] { "Enabled", "NextRunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionSchedules_OwnerType_OwnerUserId_ApplicationId",
                table: "ActionSchedules",
                columns: new[] { "OwnerType", "OwnerUserId", "ApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionSchedules_OwnerUserId",
                table: "ActionSchedules",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionSchedules_SystemIdentity",
                table: "ActionSchedules",
                column: "SystemIdentity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeys_AiProviderId",
                table: "AiApiKeys",
                column: "AiProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeys_LastUsedAt",
                table: "AiApiKeys",
                column: "LastUsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiApiKeys_ProviderName",
                table: "AiApiKeys",
                column: "ProviderName");

            migrationBuilder.CreateIndex(
                name: "IX_AiChatMessages_CreatedAt",
                table: "AiChatMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiChatMessages_SessionId",
                table: "AiChatMessages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AiChatSessions_CreatedAt",
                table: "AiChatSessions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiChatSessions_ResourceType_ResourceId",
                table: "AiChatSessions",
                columns: new[] { "ResourceType", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiChatSessions_ResourceType_ResourceId_IsActive",
                table: "AiChatSessions",
                columns: new[] { "ResourceType", "ResourceId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AiChatSessionUser_SessionId",
                table: "AiChatSessionUsers",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AiChatSessionUser_UserId",
                table: "AiChatSessionUsers",
                column: "UserId");

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

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_HashedKey",
                table: "ApiKeys",
                column: "HashedKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_LastUsedAt",
                table: "ApiKeys",
                column: "LastUsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UsedAt",
                table: "ApiKeys",
                column: "UsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeyUser_ApiKeyId",
                table: "ApiKeyUsers",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_ActiveDeploymentExecutionAttemptId",
                table: "AppDeploymentTargets",
                column: "ActiveDeploymentExecutionAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_ActiveDeploymentOperationId",
                table: "AppDeploymentTargets",
                column: "ActiveDeploymentOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_ExecutionRunnerId",
                table: "AppDeploymentTargets",
                column: "ExecutionRunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_WebAppId_DefaultConstraintKey",
                table: "AppDeploymentTargets",
                columns: new[] { "WebAppId", "DefaultConstraintKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppDeploymentTargets_WebAppId_Slug",
                table: "AppDeploymentTargets",
                columns: new[] { "WebAppId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_AppId_Category_CreatedAtUtc",
                table: "ApplicationEvents",
                columns: new[] { "AppId", "Category", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_AppId_CreatedAtUtc",
                table: "ApplicationEvents",
                columns: new[] { "AppId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_AppId_Level_CreatedAtUtc",
                table: "ApplicationEvents",
                columns: new[] { "AppId", "Level", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEvents_EventType",
                table: "ApplicationEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_AppId_DeploymentTargetId_CreatedAtUtc",
                table: "AppModuleExecutions",
                columns: new[] { "AppId", "DeploymentTargetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_AppId_DeploymentTargetId_ModuleType_Mod~",
                table: "AppModuleExecutions",
                columns: new[] { "AppId", "DeploymentTargetId", "ModuleType", "ModuleKey", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_DeploymentTargetId",
                table: "AppModuleExecutions",
                column: "DeploymentTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_HostedRunnerOperationId",
                table: "AppModuleExecutions",
                column: "HostedRunnerOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_RequestedByUserId",
                table: "AppModuleExecutions",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_RunnerId",
                table: "AppModuleExecutions",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_ScheduleOccurrenceId",
                table: "AppModuleExecutions",
                column: "ScheduleOccurrenceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppModuleExecutions_Status_CreatedAtUtc",
                table: "AppModuleExecutions",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppPermissionApprovals_AppId_Permission",
                table: "AppPermissionApprovals",
                columns: new[] { "AppId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppPermissionApprovals_TokenHash",
                table: "AppPermissionApprovals",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppVersions_AiSessionId",
                table: "AppVersions",
                column: "AiSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppVersions_AppId",
                table: "AppVersions",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_AppVersions_AppId_VersionNumber",
                table: "AppVersions",
                columns: new[] { "AppId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppVersions_CreatedBy",
                table: "AppVersions",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AppVersionSteps_AppVersionId",
                table: "AppVersionSteps",
                column: "AppVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppVersionSteps_StepId",
                table: "AppVersionSteps",
                column: "StepId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SubscriptionPlanId",
                table: "AspNetUsers",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Category_CreatedAtUtc",
                table: "AuditEvents",
                columns: new[] { "Category", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CreatedAtUtc",
                table: "AuditEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EntityType_EntityId",
                table: "AuditEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EventType",
                table: "AuditEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TargetUserId_CreatedAtUtc",
                table: "AuditEvents",
                columns: new[] { "TargetUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UserId_CreatedAtUtc",
                table: "AuditEvents",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CliAuthorizationRequests_ApiKeyId",
                table: "CliAuthorizationRequests",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_CliAuthorizationRequests_ExpiresAt",
                table: "CliAuthorizationRequests",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_CliAuthorizationRequests_UserCode",
                table: "CliAuthorizationRequests",
                column: "UserCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomStepUser_CustomStepId",
                table: "CustomStepUsers",
                column: "CustomStepId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentOperations_WebAppId_CreatedAtUtc",
                table: "DeploymentOperations",
                columns: new[] { "WebAppId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentOperations_WebAppId_Id",
                table: "DeploymentOperations",
                columns: new[] { "WebAppId", "Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSecretGrants_ExpiresAtUtc_RevokedAtUtc",
                table: "ExecutionSecretGrants",
                columns: new[] { "ExpiresAtUtc", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSecretGrants_OperationId_RevokedAtUtc_ConsumedAtUtc",
                table: "ExecutionSecretGrants",
                columns: new[] { "OperationId", "RevokedAtUtc", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSecretGrants_RunnerId",
                table: "ExecutionSecretGrants",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionSecretGrants_SecretVersionId",
                table: "ExecutionSecretGrants",
                column: "SecretVersionId");

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
                name: "IX_HostContainerExecutionOperations_HostedRunnerId_CreatedAtUtc",
                table: "HostContainerExecutionOperations",
                columns: new[] { "HostedRunnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostContainerExecutionOperations_InfrastructureRunnerId_Cre~",
                table: "HostContainerExecutionOperations",
                columns: new[] { "InfrastructureRunnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_HostedRunnerId",
                table: "HostedRunnerDeployments",
                column: "HostedRunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_Status",
                table: "HostedRunnerDeployments",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_Status_LastRecoveryAttemptUtc",
                table: "HostedRunnerDeployments",
                columns: new[] { "Status", "LastRecoveryAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_Status_ReconnectDeadlineUtc",
                table: "HostedRunnerDeployments",
                columns: new[] { "Status", "ReconnectDeadlineUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerDeployments_Status_RegistrationDeadlineUtc",
                table: "HostedRunnerDeployments",
                columns: new[] { "Status", "RegistrationDeadlineUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerPlatformSettings_DefaultSpecificationId",
                table: "HostedRunnerPlatformSettings",
                column: "DefaultSpecificationId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_ApiKeyId",
                table: "HostedRunners",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_HostedRunnerSpecificationId",
                table: "HostedRunners",
                column: "HostedRunnerSpecificationId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_RunnerHostId",
                table: "HostedRunners",
                column: "RunnerHostId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_RunnerHostId_RuntimePort",
                table: "HostedRunners",
                columns: new[] { "RunnerHostId", "RuntimePort" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_RunnerId",
                table: "HostedRunners",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_Status",
                table: "HostedRunners",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_StorageProjectId",
                table: "HostedRunners",
                column: "StorageProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunners_UserId",
                table: "HostedRunners",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerSpecificationRevisions_SpecificationId_Revision",
                table: "HostedRunnerSpecificationRevisions",
                columns: new[] { "SpecificationId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerSpecifications_Code",
                table: "HostedRunnerSpecifications",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerStorageObservations_HostedRunnerId_MeasuredAtUtc",
                table: "HostedRunnerStorageObservations",
                columns: new[] { "HostedRunnerId", "MeasuredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerStorageVerifications_HostedRunnerId_Status",
                table: "HostedRunnerStorageVerifications",
                columns: new[] { "HostedRunnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HostedRunnerStorageVerifications_IdempotencyKey",
                table: "HostedRunnerStorageVerifications",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureRunnerUpdateOperations_IdempotencyKey",
                table: "InfrastructureRunnerUpdateOperations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureRunnerUpdateOperations_RunnerHostId_Status",
                table: "InfrastructureRunnerUpdateOperations",
                columns: new[] { "RunnerHostId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureWorkflowExecutionLogs_ExecutionId_Sequence",
                table: "InfrastructureWorkflowExecutionLogs",
                columns: new[] { "ExecutionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureWorkflowExecutions_IdempotencyKey",
                table: "InfrastructureWorkflowExecutions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureWorkflowExecutions_TargetRunnerHostId_Created~",
                table: "InfrastructureWorkflowExecutions",
                columns: new[] { "TargetRunnerHostId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureWorkflowExecutions_TargetRunnerId_CreatedAtUtc",
                table: "InfrastructureWorkflowExecutions",
                columns: new[] { "TargetRunnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InvitationTokens_Email",
                table: "InvitationTokens",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationTokens_ProjectId",
                table: "InvitationTokens",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_InvitationTokens_Token",
                table: "InvitationTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCategories_IsActive_SortOrder",
                table: "MarketplaceCategories",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCategories_Name",
                table: "MarketplaceCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketplaceCategories_Slug",
                table: "MarketplaceCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAppAssignments_AppId",
                table: "OrganizationAppAssignments",
                column: "AppId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAppAssignments_AssignedBy",
                table: "OrganizationAppAssignments",
                column: "AssignedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAppAssignments_OrganizationId",
                table: "OrganizationAppAssignments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAppAssignments_OrganizationId_AppId_UserId",
                table: "OrganizationAppAssignments",
                columns: new[] { "OrganizationId", "AppId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationAppAssignments_UserId",
                table: "OrganizationAppAssignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_Email",
                table: "OrganizationInvitations",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_InvitedBy",
                table: "OrganizationInvitations",
                column: "InvitedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_OrganizationId",
                table: "OrganizationInvitations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_OrganizationId_Email_IsConsumed",
                table: "OrganizationInvitations",
                columns: new[] { "OrganizationId", "Email", "IsConsumed" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationInvitations_Token",
                table: "OrganizationInvitations",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_InvitedBy",
                table: "OrganizationMembers",
                column: "InvitedBy");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_OrganizationId",
                table: "OrganizationMembers",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_OrganizationId_UserId",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_OrganizationId_UserId_IsActive",
                table: "OrganizationMembers",
                columns: new[] { "OrganizationId", "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_UserId",
                table: "OrganizationMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_OwnerId",
                table: "Organizations",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_OwnerId_IsActive",
                table: "Organizations",
                columns: new[] { "OwnerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAiApiKeys_AddedBy",
                table: "ProjectAiApiKeys",
                column: "AddedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAiApiKeys_AiApiKeyId",
                table: "ProjectAiApiKeys",
                column: "AiApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAiApiKeys_ProjectId",
                table: "ProjectAiApiKeys",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApiKeys_AddedBy",
                table: "ProjectApiKeys",
                column: "AddedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApiKeys_ApiKeyId",
                table: "ProjectApiKeys",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApiKeys_ProjectId",
                table: "ProjectApiKeys",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApps_AddedBy",
                table: "ProjectApps",
                column: "AddedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApps_ProjectId",
                table: "ProjectApps",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectApps_WebAppId",
                table: "ProjectApps",
                column: "WebAppId");

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
                name: "IX_ProjectMembers_InvitedBy",
                table: "ProjectMembers",
                column: "InvitedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_ProjectId",
                table: "ProjectMembers",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_ProjectId_UserId_IsActive",
                table: "ProjectMembers",
                columns: new[] { "ProjectId", "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_UserId",
                table: "ProjectMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRunners_AddedBy",
                table: "ProjectRunners",
                column: "AddedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRunners_ProjectId",
                table: "ProjectRunners",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectRunners_RunnerId",
                table: "ProjectRunners",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OrganizationId",
                table: "Projects",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OrganizationId_IsActive",
                table: "Projects",
                columns: new[] { "OrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OwnerId",
                table: "Projects",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_OwnerId_IsActive",
                table: "Projects",
                columns: new[] { "OwnerId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RecentActivities_CreatedAt",
                table: "RecentActivities",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RecentActivities_UserId",
                table: "RecentActivities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecentActivities_UserId_CreatedAt",
                table: "RecentActivities",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerConnectionSessions_ApiInstanceId_DisconnectedAtUtc",
                table: "RunnerConnectionSessions",
                columns: new[] { "ApiInstanceId", "DisconnectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerConnectionSessions_ConnectionId",
                table: "RunnerConnectionSessions",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerConnectionSessions_RunnerId",
                table: "RunnerConnectionSessions",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerConnectionSessions_RunnerId_DisconnectedAtUtc",
                table: "RunnerConnectionSessions",
                columns: new[] { "RunnerId", "DisconnectedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_EnrollmentTokenHash",
                table: "RunnerEnrollments",
                column: "EnrollmentTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_HostedRunnerId",
                table: "RunnerEnrollments",
                column: "HostedRunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_HostedRunnerId_Status",
                table: "RunnerEnrollments",
                columns: new[] { "HostedRunnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_OperationId",
                table: "RunnerEnrollments",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_RunnerHostId",
                table: "RunnerEnrollments",
                column: "RunnerHostId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_RunnerHostId_Status",
                table: "RunnerEnrollments",
                columns: new[] { "RunnerHostId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerEnrollments_SecretVersionId",
                table: "RunnerEnrollments",
                column: "SecretVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerExecutionOperations_RunnerId_CompletedAtUtc",
                table: "RunnerExecutionOperations",
                columns: new[] { "RunnerId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHostProvisioningOperations_IdempotencyKey",
                table: "RunnerHostProvisioningOperations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHostProvisioningOperations_RunnerHostId_Status",
                table: "RunnerHostProvisioningOperations",
                columns: new[] { "RunnerHostId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_InfrastructureRunnerId",
                table: "RunnerHosts",
                column: "InfrastructureRunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_Provider",
                table: "RunnerHosts",
                column: "Provider");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_ProvisioningStatus",
                table: "RunnerHosts",
                column: "ProvisioningStatus");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_Region",
                table: "RunnerHosts",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_Status",
                table: "RunnerHosts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RunnerHosts_Status_HostedRunnerSchedulingEnabled",
                table: "RunnerHosts",
                columns: new[] { "Status", "HostedRunnerSchedulingEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Runners_ApiKeyId",
                table: "Runners",
                column: "ApiKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_Runners_InstallationId",
                table: "Runners",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunnerUser_RunnerId",
                table: "RunnerUsers",
                column: "RunnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleUser_ScheduleId",
                table: "ScheduleUsers",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_SecretDefinitions_OwnerType_OwnerId_DeletedAtUtc",
                table: "SecretDefinitions",
                columns: new[] { "OwnerType", "OwnerId", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecretDefinitions_OwnerType_OwnerId_Name",
                table: "SecretDefinitions",
                columns: new[] { "OwnerType", "OwnerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretVersions_ExpiresAtUtc_RevokedAtUtc",
                table: "SecretVersions",
                columns: new[] { "ExpiresAtUtc", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SecretVersions_SecretDefinitionId_Version",
                table: "SecretVersions",
                columns: new[] { "SecretDefinitionId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelfHostedRunnerEnrollments_EnrollmentTokenHash",
                table: "SelfHostedRunnerEnrollments",
                column: "EnrollmentTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelfHostedRunnerEnrollments_InstallationId",
                table: "SelfHostedRunnerEnrollments",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelfHostedRunnerEnrollments_UserId_CreatedAtUtc",
                table: "SelfHostedRunnerEnrollments",
                columns: new[] { "UserId", "CreatedAtUtc" });

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
                name: "IX_Settings_Key",
                table: "Settings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_IsActive",
                table: "SubscriptionPlans",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_IsDefault",
                table: "SubscriptionPlans",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_SortOrder",
                table: "SubscriptionPlans",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId",
                table: "UserSettings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId_SettingKey",
                table: "UserSettings",
                columns: new[] { "UserId", "SettingKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_CatalogVisibility_ReviewStatus_AccessStatus_Category",
                table: "WebApps",
                columns: new[] { "CatalogVisibility", "ReviewStatus", "AccessStatus", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_ForkedFromAppId",
                table: "WebApps",
                column: "ForkedFromAppId");

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsProfileFeatured_IsPublished",
                table: "WebApps",
                columns: new[] { "IsProfileFeatured", "IsPublished" });

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsPublished_IsForkable",
                table: "WebApps",
                columns: new[] { "IsPublished", "IsForkable" });

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsPublished_MarketplaceStatus_Category",
                table: "WebApps",
                columns: new[] { "IsPublished", "MarketplaceStatus", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsPublished_ReviewStatus_AccessStatus_Category",
                table: "WebApps",
                columns: new[] { "IsPublished", "ReviewStatus", "AccessStatus", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_IsSample_IsPublished",
                table: "WebApps",
                columns: new[] { "IsSample", "IsPublished" });

            migrationBuilder.CreateIndex(
                name: "IX_WebApps_PublicUrl",
                table: "WebApps",
                column: "PublicUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebAppScreenshots_WebAppId",
                table: "WebAppScreenshots",
                column: "WebAppId");

            migrationBuilder.CreateIndex(
                name: "IX_WebAppScreenshots_WebAppId_SortOrder",
                table: "WebAppScreenshots",
                columns: new[] { "WebAppId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_WebAppStars_UserId",
                table: "WebAppStars",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WebAppStars_WebAppId",
                table: "WebAppStars",
                column: "WebAppId");

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

            migrationBuilder.CreateIndex(
                name: "IX_WebAppUser_WebAppId",
                table: "WebAppUsers",
                column: "WebAppId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountStorageAllocations");

            migrationBuilder.DropTable(
                name: "ActionScheduleOccurrences");

            migrationBuilder.DropTable(
                name: "AiChatMessages");

            migrationBuilder.DropTable(
                name: "AiChatSessionUsers");

            migrationBuilder.DropTable(
                name: "AiGuidanceDocuments");

            migrationBuilder.DropTable(
                name: "AiModels");

            migrationBuilder.DropTable(
                name: "ApiKeyUsers");

            migrationBuilder.DropTable(
                name: "ApplicationEvents");

            migrationBuilder.DropTable(
                name: "AppModuleExecutions");

            migrationBuilder.DropTable(
                name: "AppPermissionApprovals");

            migrationBuilder.DropTable(
                name: "AppVersionSteps");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "CliAuthorizationRequests");

            migrationBuilder.DropTable(
                name: "CustomStepUsers");

            migrationBuilder.DropTable(
                name: "DeploymentOperations");

            migrationBuilder.DropTable(
                name: "ExecutionSecretGrants");

            migrationBuilder.DropTable(
                name: "FlowExecutionLogs");

            migrationBuilder.DropTable(
                name: "FlowExecutionUsers");

            migrationBuilder.DropTable(
                name: "FlowUsers");

            migrationBuilder.DropTable(
                name: "HostContainerExecutionOperations");

            migrationBuilder.DropTable(
                name: "HostedRunnerPlatformSettings");

            migrationBuilder.DropTable(
                name: "HostedRunnerSpecificationRevisions");

            migrationBuilder.DropTable(
                name: "HostedRunnerStorageObservations");

            migrationBuilder.DropTable(
                name: "HostedRunnerStorageVerifications");

            migrationBuilder.DropTable(
                name: "InfrastructureRunnerUpdateOperations");

            migrationBuilder.DropTable(
                name: "InfrastructureWorkflowExecutionLogs");

            migrationBuilder.DropTable(
                name: "InvitationTokens");

            migrationBuilder.DropTable(
                name: "MarketplaceCategories");

            migrationBuilder.DropTable(
                name: "OrganizationAppAssignments");

            migrationBuilder.DropTable(
                name: "OrganizationInvitations");

            migrationBuilder.DropTable(
                name: "OrganizationMembers");

            migrationBuilder.DropTable(
                name: "ProjectAiApiKeys");

            migrationBuilder.DropTable(
                name: "ProjectApiKeys");

            migrationBuilder.DropTable(
                name: "ProjectApps");

            migrationBuilder.DropTable(
                name: "ProjectCustomSteps");

            migrationBuilder.DropTable(
                name: "ProjectFlows");

            migrationBuilder.DropTable(
                name: "ProjectMembers");

            migrationBuilder.DropTable(
                name: "ProjectRunners");

            migrationBuilder.DropTable(
                name: "RecentActivities");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RunnerConnectionSessions");

            migrationBuilder.DropTable(
                name: "RunnerEnrollments");

            migrationBuilder.DropTable(
                name: "RunnerExecutionOperations");

            migrationBuilder.DropTable(
                name: "RunnerHostProvisioningOperations");

            migrationBuilder.DropTable(
                name: "RunnerUsers");

            migrationBuilder.DropTable(
                name: "ScheduleUsers");

            migrationBuilder.DropTable(
                name: "SelfHostedRunnerEnrollments");

            migrationBuilder.DropTable(
                name: "SensitiveVariableUsers");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "UserGitHubTokens");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "WebAppScreenshots");

            migrationBuilder.DropTable(
                name: "WebAppStars");

            migrationBuilder.DropTable(
                name: "WebAppSteps");

            migrationBuilder.DropTable(
                name: "WebAppUsers");

            migrationBuilder.DropTable(
                name: "ActionSchedules");

            migrationBuilder.DropTable(
                name: "AiChatSessions");

            migrationBuilder.DropTable(
                name: "AppDeploymentTargets");

            migrationBuilder.DropTable(
                name: "AppVersions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "FlowExecutions");

            migrationBuilder.DropTable(
                name: "InfrastructureWorkflowExecutions");

            migrationBuilder.DropTable(
                name: "AiApiKeys");

            migrationBuilder.DropTable(
                name: "Flows");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "HostedRunnerDeployments");

            migrationBuilder.DropTable(
                name: "SecretVersions");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropTable(
                name: "SensitiveVariables");

            migrationBuilder.DropTable(
                name: "CustomSteps");

            migrationBuilder.DropTable(
                name: "WebApps");

            migrationBuilder.DropTable(
                name: "AiProviders");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "HostedRunners");

            migrationBuilder.DropTable(
                name: "SecretDefinitions");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "HostedRunnerSpecifications");

            migrationBuilder.DropTable(
                name: "RunnerHosts");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropTable(
                name: "Runners");

            migrationBuilder.DropTable(
                name: "ApiKeys");
        }
    }
}
