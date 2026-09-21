using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;

namespace Automaton.Api.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid, IdentityUserClaim<Guid>,
        ApplicationUserRole, IdentityUserLogin<Guid>, IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>, IDataContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        private IDbContextTransaction _transaction;

        public virtual DbSet<RefreshToken> RefreshTokens { get; set; }
        public virtual DbSet<Flow> Flows { get; set; }
        public virtual DbSet<FlowUser> FlowUsers { get; set; }
        public virtual DbSet<FlowExecution> FlowExecutions { get; set; }
        public virtual DbSet<FlowExecutionUser> FlowExecutionUsers { get; set; }
        public virtual DbSet<FlowExecutionLog> FlowExecutionLogs { get; set; }
        public virtual DbSet<AuditEvent> AuditEvents { get; set; }
        public virtual DbSet<Runner> Runners { get; set; }
        public virtual DbSet<RunnerConnectionSession> RunnerConnectionSessions { get; set; }
        public virtual DbSet<RunnerUser> RunnerUsers { get; set; }
        public virtual DbSet<RunnerHost> RunnerHosts { get; set; }
        public virtual DbSet<RunnerHostProvisioningOperation> RunnerHostProvisioningOperations { get; set; }
        public virtual DbSet<InfrastructureRunnerUpdateOperation> InfrastructureRunnerUpdateOperations { get; set; }
        public virtual DbSet<RunnerEnrollment> RunnerEnrollments { get; set; }
        public virtual DbSet<SelfHostedRunnerEnrollment> SelfHostedRunnerEnrollments { get; set; }
        public virtual DbSet<InfrastructureWorkflowExecution> InfrastructureWorkflowExecutions { get; set; }
        public virtual DbSet<InfrastructureWorkflowExecutionLog> InfrastructureWorkflowExecutionLogs { get; set; }
        public virtual DbSet<HostedRunner> HostedRunners { get; set; }
        public virtual DbSet<HostedRunnerSpecification> HostedRunnerSpecifications { get; set; }
        public virtual DbSet<HostedRunnerSpecificationRevision> HostedRunnerSpecificationRevisions { get; set; }
        public virtual DbSet<HostedRunnerPlatformSettings> HostedRunnerPlatformSettings { get; set; }
        public virtual DbSet<HostedRunnerDeployment> HostedRunnerDeployments { get; set; }
        public virtual DbSet<HostedRunnerStorageObservation> HostedRunnerStorageObservations { get; set; }
        public virtual DbSet<HostedRunnerStorageVerification> HostedRunnerStorageVerifications { get; set; }
        public virtual DbSet<CustomStep> CustomSteps { get; set; }
        public virtual DbSet<CustomStepUser> CustomStepUsers { get; set; }
        public virtual DbSet<Schedule> Schedules { get; set; }
        public virtual DbSet<ScheduleUser> ScheduleUsers { get; set; }
        public virtual DbSet<RecentActivity> RecentActivities { get; set; }
        public virtual DbSet<ApiKey> ApiKeys { get; set; }
        public virtual DbSet<ApiKeyUser> ApiKeyUsers { get; set; }
        public virtual DbSet<CliAuthorizationRequest> CliAuthorizationRequests { get; set; }
        public virtual DbSet<AiApiKey> AiApiKeys { get; set; }
        public virtual DbSet<AiProvider> AiProviders { get; set; }
        public virtual DbSet<AiModel> AiModels { get; set; }
        public virtual DbSet<AiGuidanceDocument> AiGuidanceDocuments { get; set; }
        public virtual DbSet<WebApp> WebApps { get; set; }
        public virtual DbSet<AppDeploymentTarget> AppDeploymentTargets { get; set; }
        public virtual DbSet<DeploymentOperation> DeploymentOperations { get; set; }
        public virtual DbSet<MarketplaceCategory> MarketplaceCategories { get; set; }
        public virtual DbSet<WebAppStar> WebAppStars { get; set; }
        public virtual DbSet<WebAppScreenshot> WebAppScreenshots { get; set; }
        public virtual DbSet<WebAppUser> WebAppUsers { get; set; }
        public virtual DbSet<WebAppStep> WebAppSteps { get; set; }
        public virtual DbSet<AppVersion> AppVersions { get; set; }
        public virtual DbSet<AppVersionStep> AppVersionSteps { get; set; }
        public virtual DbSet<AppModuleExecution> AppModuleExecutions { get; set; }
        public virtual DbSet<ActionSchedule> ActionSchedules { get; set; }
        public virtual DbSet<ActionScheduleOccurrence> ActionScheduleOccurrences { get; set; }
        public virtual DbSet<SecretDefinition> SecretDefinitions { get; set; }
        public virtual DbSet<SecretVersion> SecretVersions { get; set; }
        public virtual DbSet<ExecutionSecretGrant> ExecutionSecretGrants { get; set; }
        public virtual DbSet<Setting> Settings { get; set; }
        public virtual DbSet<SensitiveVariable> SensitiveVariables { get; set; }
        public virtual DbSet<SensitiveVariableUser> SensitiveVariableUsers { get; set; }
        
        // GitHub integration
        public virtual DbSet<UserGitHubToken> UserGitHubTokens { get; set; }
        
        // Project/Organization entities
        public virtual DbSet<Project> Projects { get; set; }
        public virtual DbSet<ProjectMember> ProjectMembers { get; set; }
        public virtual DbSet<ProjectApp> ProjectApps { get; set; }
        public virtual DbSet<ProjectFlow> ProjectFlows { get; set; }
        public virtual DbSet<ProjectRunner> ProjectRunners { get; set; }
        public virtual DbSet<ProjectCustomStep> ProjectCustomSteps { get; set; }
        public virtual DbSet<ProjectApiKey> ProjectApiKeys { get; set; }
        public virtual DbSet<ProjectAiApiKey> ProjectAiApiKeys { get; set; }
        public virtual DbSet<InvitationToken> InvitationTokens { get; set; }
        
        // Organization entities
        public virtual DbSet<Organization> Organizations { get; set; }
        public virtual DbSet<OrganizationMember> OrganizationMembers { get; set; }
        public virtual DbSet<OrganizationAppAssignment> OrganizationAppAssignments { get; set; }
        public virtual DbSet<OrganizationInvitation> OrganizationInvitations { get; set; }

        // AI Chat History entities
        public virtual DbSet<AiChatSession> AiChatSessions { get; set; }
        public virtual DbSet<AiChatMessage> AiChatMessages { get; set; }
        public virtual DbSet<AiChatSessionUser> AiChatSessionUsers { get; set; }
        public virtual DbSet<ApplicationEvent> ApplicationEvents { get; set; }

        // User Settings
        public virtual DbSet<UserSetting> UserSettings { get; set; }

        // Subscription plans
        public virtual DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // No-op, configuration is handled by DI
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ApplicationUserRole>(userRole =>
            {
                userRole.HasKey(ur => new { ur.UserId, ur.RoleId });

                userRole.HasOne(ur => ur.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(ur => ur.RoleId)
                    .IsRequired();

                userRole.HasOne(ur => ur.User)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(ur => ur.UserId)
                    .IsRequired();
            });

            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.HasOne(e => e.SubscriptionPlan)
                    .WithMany()
                    .HasForeignKey(e => e.SubscriptionPlanId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.SubscriptionPlanId);
            });

            modelBuilder.Entity<ActionSchedule>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ActionKey).IsRequired().HasMaxLength(64);
                entity.Property(e => e.OwnerType).IsRequired().HasMaxLength(16);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.ExecutionScope).IsRequired().HasMaxLength(32);
                entity.Property(e => e.ScheduleType).IsRequired().HasMaxLength(16);
                entity.Property(e => e.CronExpression).HasMaxLength(120);
                entity.Property(e => e.TimeZone).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ConcurrencyPolicy).IsRequired().HasMaxLength(32);
                entity.Property(e => e.MisfirePolicy).IsRequired().HasMaxLength(32);
                entity.Property(e => e.InputJson).IsRequired().HasColumnType("longtext");
                entity.Property(e => e.LeaseOwner).HasMaxLength(128);
                entity.HasIndex(e => new { e.OwnerType, e.OwnerUserId, e.ApplicationId });
                entity.HasIndex(e => new { e.Enabled, e.NextRunAtUtc });
                entity.HasIndex(e => e.SystemIdentity).IsUnique();
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
                entity.ToTable(table => table.HasCheckConstraint(
                    "CK_ActionSchedules_Owner",
                    "(`OwnerType` = 'User' AND `OwnerUserId` IS NOT NULL) OR (`OwnerType` = 'System' AND `OwnerUserId` IS NULL)"));
            });

            modelBuilder.Entity<ActionScheduleOccurrence>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(160);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
                entity.Property(e => e.FailureSummary).HasMaxLength(2000);
                entity.HasIndex(e => e.IdempotencyKey).IsUnique();
                entity.HasIndex(e => new { e.ScheduleId, e.Status, e.ScheduledForUtc });
                entity.HasOne(e => e.Schedule).WithMany().HasForeignKey(e => e.ScheduleId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Flow>(entity =>
            {
                entity.HasKey(e => new { e.Id });
            });

            modelBuilder.Entity<FlowUser>(entity =>
            {
                entity.HasKey(e => new { e.FlowId, e.UserId });
                entity.HasIndex(e => e.FlowId, "IX_FlowUser_FlowId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.FlowId).IsRequired();
            });

            modelBuilder.Entity<FlowExecution>(entity =>
            {
                entity.HasKey(e => new { e.Id });
            });

            modelBuilder.Entity<AuditEvent>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Category).IsRequired().HasMaxLength(80);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(80);
                entity.Property(e => e.EntityType).IsRequired().HasMaxLength(80);
                entity.Property(e => e.UserName).HasMaxLength(256);
                entity.Property(e => e.Outcome).IsRequired().HasMaxLength(32);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(128);
                entity.Property(e => e.Message).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.MetadataJson).HasColumnType("longtext");
                entity.Property(e => e.IpAddress).HasMaxLength(128);
                entity.Property(e => e.CreatedAtUtc).IsRequired();

                entity.HasIndex(e => e.CreatedAtUtc);
                entity.HasIndex(e => new { e.Category, e.CreatedAtUtc });
                entity.HasIndex(e => new { e.EntityType, e.EntityId });
                entity.HasIndex(e => new { e.UserId, e.CreatedAtUtc });
                entity.HasIndex(e => new { e.TargetUserId, e.CreatedAtUtc });
                entity.HasIndex(e => e.EventType);
            });

            modelBuilder.Entity<ApplicationEvent>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Level).IsRequired().HasMaxLength(32);
                entity.Property(e => e.Category).IsRequired().HasMaxLength(80);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(128);
                entity.Property(e => e.Message).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.MetadataJson).HasColumnType("longtext");
                entity.Property(e => e.CreatedAtUtc).IsRequired();

                entity.HasIndex(e => new { e.AppId, e.CreatedAtUtc });
                entity.HasIndex(e => new { e.AppId, e.Category, e.CreatedAtUtc });
                entity.HasIndex(e => new { e.AppId, e.Level, e.CreatedAtUtc });
                entity.HasIndex(e => e.EventType);
            });

            modelBuilder.Entity<AppModuleExecution>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ModuleType).IsRequired().HasMaxLength(40);
                entity.Property(e => e.ModuleKey).IsRequired().HasMaxLength(64);
                entity.Property(e => e.TriggerType).IsRequired().HasMaxLength(40);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(40);
                entity.Property(e => e.DeploymentHash).HasMaxLength(80);
                entity.Property(e => e.ErrorCode).HasMaxLength(80);
                entity.Property(e => e.FailureSummary).HasMaxLength(1000);
                entity.Property(e => e.CorrelationId).HasMaxLength(128);
                entity.Property(e => e.ResultFingerprint).HasMaxLength(64);
                entity.HasIndex(e => new { e.AppId, e.DeploymentTargetId, e.CreatedAtUtc });
                entity.HasIndex(e => new { e.AppId, e.DeploymentTargetId, e.ModuleType, e.ModuleKey, e.CreatedAtUtc });
                entity.HasIndex(e => e.HostedRunnerOperationId).IsUnique();
                entity.HasIndex(e => e.ScheduleOccurrenceId).IsUnique();
                entity.HasOne(e => e.HostedRunnerOperation)
                    .WithOne(e => e.AppModuleExecution)
                    .HasForeignKey<AppModuleExecution>(e => e.HostedRunnerOperationId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(e => new { e.Status, e.CreatedAtUtc });
                entity.HasOne<WebApp>().WithMany().HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<AppDeploymentTarget>()
                    .WithMany()
                    .HasForeignKey(e => e.DeploymentTargetId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<Runner>().WithMany().HasForeignKey(e => e.RunnerId).OnDelete(DeleteBehavior.SetNull);
                entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.RequestedByUserId).OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<FlowExecutionUser>(entity =>
            {
                entity.HasKey(e => new { e.FlowExecutionId, e.UserId });
                entity.HasIndex(e => e.FlowExecutionId, "IX_FlowExecutionUser_FlowExecutionId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.FlowExecutionId).IsRequired();
            });

            modelBuilder.Entity<Runner>(entity =>
            {
                entity.HasKey(e => new { e.Id });
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.Property(e => e.Version).HasMaxLength(50);
                entity.Property(e => e.MachineName).HasMaxLength(120);
                entity.Property(e => e.IpAddress).HasMaxLength(64);
                entity.Property(e => e.ApiInstanceId).HasMaxLength(80);
                entity.Property(e => e.ApiColor).HasMaxLength(32);
                entity.Property(e => e.ImageDigest).HasMaxLength(256);
                entity.Property(e => e.ApiCompatibilityVersion).HasMaxLength(40);
                entity.Property(e => e.OperatingSystem).HasMaxLength(256);
                entity.Property(e => e.Architecture).HasMaxLength(40);
                entity.Property(e => e.ContainerHealth).HasMaxLength(40);
                entity.Property(e => e.Purpose).IsRequired().HasMaxLength(40).HasDefaultValue(RunnerPurposes.Managed);
                entity.Property(e => e.ApplicationProfile).HasMaxLength(40);
                entity.Property(e => e.CapabilitiesJson).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.InstallationId).HasMaxLength(128);
                entity.HasIndex(e => e.ApiKeyId);
                entity.HasOne(e => e.ApiKey)
                    .WithMany()
                    .HasForeignKey(e => e.ApiKeyId)
                    .OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(e => e.InstallationId).IsUnique();
            });

            modelBuilder.Entity<SelfHostedRunnerEnrollment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.RunnerName).IsRequired().HasMaxLength(120);
                entity.Property(e => e.ApplicationProfile).IsRequired().HasMaxLength(40);
                entity.Property(e => e.EnrollmentTokenHash).IsRequired().HasMaxLength(128);
                entity.Property(e => e.InstallationId).IsRequired().HasMaxLength(128);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
                entity.HasIndex(e => e.EnrollmentTokenHash).IsUnique();
                entity.HasIndex(e => e.InstallationId).IsUnique();
                entity.HasIndex(e => new { e.UserId, e.CreatedAtUtc });
            });

            modelBuilder.Entity<RunnerConnectionSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ConnectionId).IsRequired().HasMaxLength(120);
                entity.Property(e => e.ConnectedAtUtc).IsRequired();
                entity.Property(e => e.DisconnectReason).HasMaxLength(80);
                entity.Property(e => e.ApiInstanceId).HasMaxLength(80);
                entity.Property(e => e.ApiColor).HasMaxLength(32);

                entity.HasIndex(e => e.RunnerId);
                entity.HasIndex(e => new { e.RunnerId, e.DisconnectedAtUtc });
                entity.HasIndex(e => e.ConnectionId);
                entity.HasIndex(e => new { e.ApiInstanceId, e.DisconnectedAtUtc });

                entity.HasOne(e => e.Runner)
                    .WithMany()
                    .HasForeignKey(e => e.RunnerId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RunnerUser>(entity =>
            {
                entity.HasKey(e => new { e.RunnerId, e.UserId });
                entity.HasIndex(e => e.RunnerId, "IX_RunnerUser_RunnerId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.RunnerId).IsRequired();
            });

            modelBuilder.Entity<RunnerHost>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Provider).IsRequired().HasMaxLength(80);
                entity.Property(e => e.ProviderResourceId).HasMaxLength(120);
                entity.Property(e => e.ProviderServerType).HasMaxLength(80);
                entity.Property(e => e.ProviderImageId).HasMaxLength(120);
                entity.Property(e => e.Region).HasMaxLength(80);
                entity.Property(e => e.PublicIp).HasMaxLength(120);
                entity.Property(e => e.PrivateIp).HasMaxLength(120);
                entity.Property(e => e.ProvisioningTransport).IsRequired().HasMaxLength(40);
                entity.Property(e => e.RuntimeConnectivityMode).IsRequired().HasMaxLength(40);
                entity.Property(e => e.HostedRunnerSchedulingEnabled).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(40);
                entity.Property(e => e.ProvisioningStatus).IsRequired().HasMaxLength(40).HasDefaultValue(RunnerHostProvisioningStatuses.Manual);
                entity.Property(e => e.ProvisioningFailureCode).HasMaxLength(80);
                entity.Property(e => e.ProvisioningFailureMessage).HasMaxLength(2000);
                entity.Property(e => e.TotalCpuCores).IsRequired().HasColumnType("decimal(5,2)");
                entity.Property(e => e.ReservedCpuCores).IsRequired().HasColumnType("decimal(5,2)");
                entity.Property(e => e.TotalMemoryMb).IsRequired();
                entity.Property(e => e.ReservedMemoryMb).IsRequired();
                entity.Property(e => e.TotalDiskMb).IsRequired();
                entity.Property(e => e.ReservedDiskMb).IsRequired();
                entity.Property(e => e.MetadataJson).HasColumnType("json");
                entity.Property(e => e.HealthJson).HasColumnType("json");
                entity.Property(e => e.StorageState).IsRequired().HasMaxLength(32).HasDefaultValue(RunnerHostStorageStates.Unknown);
                entity.Property(e => e.StorageMeasurementStatus).IsRequired().HasMaxLength(32).HasDefaultValue(StorageMeasurementStatuses.NeverMeasured);
                entity.Property(e => e.StorageMeasurementError).HasMaxLength(2000);
                entity.Property(e => e.CreatedAt).IsRequired();

                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.ProvisioningStatus);
                entity.HasIndex(e => new { e.Status, e.HostedRunnerSchedulingEnabled });
                entity.HasIndex(e => e.Provider);
                entity.HasIndex(e => e.Region);
                entity.HasIndex(e => e.InfrastructureRunnerId);

                entity.HasOne(e => e.InfrastructureRunner)
                    .WithMany()
                    .HasForeignKey(e => e.InfrastructureRunnerId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<RunnerHostProvisioningOperation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(40);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(40);
                entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(120);
                entity.Property(e => e.ProviderResourceId).HasMaxLength(120);
                entity.Property(e => e.FailureCode).HasMaxLength(80);
                entity.Property(e => e.Error).HasMaxLength(2000);
                entity.Property(e => e.CloudInitTemplateVersion).HasMaxLength(40);
                entity.HasIndex(e => e.IdempotencyKey).IsUnique();
                entity.HasIndex(e => new { e.RunnerHostId, e.Status });
                entity.HasOne(e => e.RunnerHost).WithMany().HasForeignKey(e => e.RunnerHostId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<InfrastructureRunnerUpdateOperation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(40);
                entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(160);
                entity.Property(e => e.RequestedImage).HasMaxLength(256);
                entity.Property(e => e.TargetImageDigest).HasMaxLength(256);
                entity.Property(e => e.PreviousImageDigest).HasMaxLength(256);
                entity.Property(e => e.Error).HasMaxLength(2000);
                entity.Property(e => e.Diagnostics).HasColumnType("longtext");
                entity.HasIndex(e => e.IdempotencyKey).IsUnique();
                entity.HasIndex(e => new { e.RunnerHostId, e.Status });
            });

            modelBuilder.Entity<RunnerEnrollment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ExpectedPurpose).IsRequired().HasMaxLength(40);
                entity.Property(e => e.ExpectedCapabilitiesJson).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.EnrollmentTokenHash).IsRequired().HasMaxLength(128);
                entity.Property(e => e.InstallationId).IsRequired().HasMaxLength(128);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
                entity.Property(e => e.FailureReason).HasMaxLength(1000);

                entity.HasIndex(e => e.RunnerHostId);
                entity.HasIndex(e => e.HostedRunnerId);
                entity.HasIndex(e => e.OperationId);
                entity.HasIndex(e => e.EnrollmentTokenHash).IsUnique();
                entity.HasIndex(e => new { e.RunnerHostId, e.Status });
                entity.HasIndex(e => new { e.HostedRunnerId, e.Status });
                entity.HasOne(e => e.RunnerHost)
                    .WithMany(e => e.RunnerEnrollments)
                    .HasForeignKey(e => e.RunnerHostId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<HostedRunner>()
                    .WithMany()
                    .HasForeignKey(e => e.HostedRunnerId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Operation)
                    .WithMany()
                    .HasForeignKey(e => e.OperationId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<SecretVersion>()
                    .WithMany()
                    .HasForeignKey(e => e.SecretVersionId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<SecretDefinition>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OwnerType).IsRequired().HasMaxLength(40);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
                entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.Name }).IsUnique();
                entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.DeletedAtUtc });
            });

            modelBuilder.Entity<SecretVersion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProtectedValue).IsRequired().HasColumnType("longtext");
                entity.HasIndex(e => new { e.SecretDefinitionId, e.Version }).IsUnique();
                entity.HasIndex(e => new { e.ExpiresAtUtc, e.RevokedAtUtc });
                entity.HasOne(e => e.SecretDefinition)
                    .WithMany(e => e.Versions)
                    .HasForeignKey(e => e.SecretDefinitionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ExecutionSecretGrant>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ActionKey).IsRequired().HasMaxLength(64);
                entity.Property(e => e.DeliveryType).IsRequired().HasMaxLength(40);
                entity.Property(e => e.DeliveryPolicy)
                    .IsRequired()
                    .HasMaxLength(40)
                    .HasDefaultValue(SecretDeliveryPolicies.SingleDelivery);
                entity.Property(e => e.TargetName).IsRequired().HasMaxLength(200);
                entity.HasIndex(e => new { e.OperationId, e.RevokedAtUtc, e.ConsumedAtUtc });
                entity.HasIndex(e => new { e.ExpiresAtUtc, e.RevokedAtUtc });
                entity.HasIndex(e => e.RunnerId);
                entity.HasOne(e => e.SecretVersion)
                    .WithMany(e => e.Grants)
                    .HasForeignKey(e => e.SecretVersionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<InfrastructureWorkflowExecution>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.WorkflowVersionId).IsRequired().HasMaxLength(80);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
                entity.Property(e => e.Error).HasMaxLength(2000);
                entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(160);
                entity.Property(e => e.ResultJson).HasColumnType("longtext");
                entity.HasIndex(e => e.IdempotencyKey).IsUnique();
                entity.HasIndex(e => new { e.TargetRunnerHostId, e.CreatedAtUtc });
                entity.HasIndex(e => new { e.TargetRunnerId, e.CreatedAtUtc });
            });

            modelBuilder.Entity<InfrastructureWorkflowExecutionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Stream).IsRequired().HasMaxLength(16);
                entity.Property(e => e.Message).IsRequired().HasColumnType("longtext");
                entity.HasIndex(e => new { e.ExecutionId, e.Sequence }).IsUnique();
                entity.HasOne(e => e.Execution)
                    .WithMany(e => e.Logs)
                    .HasForeignKey(e => e.ExecutionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<HostedRunner>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.StorageProjectId).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(40);
                entity.Property(e => e.ContainerName).IsRequired().HasMaxLength(160);
                entity.Property(e => e.ContainerId).HasMaxLength(160);
                entity.Property(e => e.LastError).HasMaxLength(2000);
                entity.Property(e => e.StorageMeasurementSource).IsRequired().HasMaxLength(40).HasDefaultValue(StorageMeasurementSources.RunnerSelfReported);
                entity.Property(e => e.StorageMeasurementStatus).IsRequired().HasMaxLength(32).HasDefaultValue(StorageMeasurementStatuses.NeverMeasured);
                entity.Property(e => e.StorageState).IsRequired().HasMaxLength(32).HasDefaultValue(HostedRunnerStorageStates.Unknown);
                entity.Property(e => e.StorageMeasurementError).HasMaxLength(2000);
                entity.Property(e => e.StorageUsagePercent).HasColumnType("decimal(7,2)");
                entity.Property(e => e.StorageSelfReportedStatus).IsRequired().HasMaxLength(32).HasDefaultValue(StorageMeasurementStatuses.NeverMeasured);
                entity.Property(e => e.StorageSelfReportedError).HasMaxLength(2000);
                entity.Property(e => e.EnforcementState).IsRequired().HasMaxLength(32).HasDefaultValue(HostedRunnerEnforcementStates.None);
                entity.Property(e => e.LastEnforcementReason).HasMaxLength(2000);
                entity.Property(e => e.CreatedAt).IsRequired();
                ConfigureJson(entity.Property(e => e.SpecificationSnapshot));
                ConfigureJson(entity.Property(e => e.PendingSpecificationSnapshot));

                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.RunnerHostId);
                entity.HasIndex(e => new { e.RunnerHostId, e.RuntimePort }).IsUnique();
                entity.HasIndex(e => e.RunnerId);
                entity.HasIndex(e => e.ApiKeyId);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.HostedRunnerSpecification)
                    .WithMany()
                    .HasForeignKey(e => e.HostedRunnerSpecificationId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.RunnerHost)
                    .WithMany(e => e.HostedRunners)
                    .HasForeignKey(e => e.RunnerHostId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Runner)
                    .WithMany()
                    .HasForeignKey(e => e.RunnerId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.ApiKey)
                    .WithMany()
                    .HasForeignKey(e => e.ApiKeyId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<HostedRunnerStorageObservation>(entity =>
            {
                entity.Property(e => e.Source).IsRequired().HasMaxLength(40);
                entity.Property(e => e.Error).HasMaxLength(2000);
                entity.Property(e => e.RunnerVersion).HasMaxLength(80);
                entity.HasIndex(e => new { e.HostedRunnerId, e.MeasuredAtUtc });
                entity.HasOne(e => e.HostedRunner).WithMany(e => e.StorageObservations)
                    .HasForeignKey(e => e.HostedRunnerId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<HostedRunnerStorageVerification>(entity =>
            {
                entity.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(160);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
                entity.Property(e => e.Reason).IsRequired().HasMaxLength(80);
                entity.Property(e => e.Error).HasMaxLength(2000);
                entity.HasIndex(e => e.IdempotencyKey).IsUnique();
                entity.HasIndex(e => new { e.HostedRunnerId, e.Status });
                entity.HasOne(e => e.HostedRunner).WithMany(e => e.StorageVerifications)
                    .HasForeignKey(e => e.HostedRunnerId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<HostedRunnerSpecification>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(80);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
                entity.Property(e => e.Description).HasMaxLength(2000);
                ConfigureJson(entity.Property(e => e.Resources));
            });

            modelBuilder.Entity<HostedRunnerSpecificationRevision>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
                entity.Property(e => e.Description).HasMaxLength(2000);
                ConfigureJson(entity.Property(e => e.Resources));
                entity.HasIndex(e => new { e.SpecificationId, e.Revision }).IsUnique();
                entity.HasOne(e => e.Specification)
                    .WithMany(e => e.Revisions)
                    .HasForeignKey(e => e.SpecificationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<HostedRunnerPlatformSettings>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.DefaultSpecification)
                    .WithMany()
                    .HasForeignKey(e => e.DefaultSpecificationId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<HostedRunnerDeployment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Action).IsRequired().HasMaxLength(40);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(40);
                entity.Property(e => e.CommandPreview).HasColumnType("longtext");
                entity.Property(e => e.Logs).HasColumnType("longtext");
                entity.Property(e => e.Error).HasColumnType("longtext");
                entity.Property(e => e.PreviousConnectionId).HasMaxLength(120);
                entity.Property(e => e.StartedAt).IsRequired();

                entity.HasIndex(e => e.HostedRunnerId);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => new { e.Status, e.RegistrationDeadlineUtc });
                entity.HasIndex(e => new { e.Status, e.ReconnectDeadlineUtc });
                entity.HasIndex(e => new { e.Status, e.LastRecoveryAttemptUtc });

                entity.HasOne(e => e.HostedRunner)
                    .WithMany(e => e.Deployments)
                    .HasForeignKey(e => e.HostedRunnerId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Schedule>(entity =>
            {
                entity.HasKey(e => new { e.Id });
            });

            modelBuilder.Entity<ScheduleUser>(entity =>
            {
                entity.HasKey(e => new { e.ScheduleId, e.UserId });
                entity.HasIndex(e => e.ScheduleId, "IX_ScheduleUser_ScheduleId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.ScheduleId).IsRequired();
            });

            modelBuilder.Entity<CustomStep>(entity =>
            {
                entity.HasKey(e => new { e.Id });
            });

            modelBuilder.Entity<CustomStepUser>(entity =>
            {
                entity.HasKey(e => new { e.CustomStepId, e.UserId });
                entity.HasIndex(e => e.CustomStepId, "IX_CustomStepUser_CustomStepId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.CustomStepId).IsRequired();
            });

            modelBuilder.Entity<WebApp>(entity =>
            {
                entity.HasKey(e => new { e.Id });
                entity.HasIndex(e => e.PublicUrl).IsUnique();

                // Analytics fields
                entity.Property(e => e.ViewCount).IsRequired().HasDefaultValue(0);
                entity.Property(e => e.StarCount).IsRequired().HasDefaultValue(0);
                entity.Property(e => e.LastViewedAt).IsRequired(false);

                // Sample/Featured flags
                entity.Property(e => e.IsSample).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.IsFeatured).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.IsProfileFeatured).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.IsForkable).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.RuntimeVisibility)
                    .IsRequired()
                    .HasMaxLength(40)
                    .HasDefaultValue(AppRuntimeVisibilities.Private);
                entity.Property(e => e.CatalogVisibility)
                    .IsRequired()
                    .HasMaxLength(40)
                    .HasDefaultValue(AppCatalogVisibilities.Unlisted);
                entity.Property(e => e.MarketplacePreviewMode)
                    .IsRequired()
                    .HasMaxLength(40)
                    .HasDefaultValue(MarketplacePreviewModes.Launchable);
                entity.Property(e => e.MarketplaceStatus)
                    .IsRequired()
                    .HasMaxLength(40)
                    .HasDefaultValue(MarketplaceAppStatuses.Approved);
                entity.Property(e => e.ReviewStatus)
                    .IsRequired()
                    .HasMaxLength(40)
                    .HasDefaultValue(AppReviewStatuses.Approved);
                entity.Property(e => e.AccessStatus)
                    .IsRequired()
                    .HasMaxLength(40)
                    .HasDefaultValue(AppAccessStatuses.Active);
                entity.Property(e => e.MarketplaceModerationReason)
                    .HasMaxLength(2000);
                entity.Property(e => e.ForkedFromAppName)
                    .HasMaxLength(200);
                entity.Property(e => e.ForkedFromAuthorName)
                    .HasMaxLength(200);

                // Index for sample apps queries
                entity.HasIndex(e => new { e.IsSample, e.IsPublished });
                entity.HasIndex(e => new { e.IsProfileFeatured, e.IsPublished });
                entity.HasIndex(e => new { e.IsPublished, e.MarketplaceStatus, e.Category });
                entity.HasIndex(e => new { e.IsPublished, e.ReviewStatus, e.AccessStatus, e.Category });
                entity.HasIndex(e => e.ForkedFromAppId);
                entity.HasIndex(e => new { e.IsPublished, e.IsForkable });
                entity.HasIndex(e => new { e.CatalogVisibility, e.ReviewStatus, e.AccessStatus, e.Category });

                entity.HasMany(e => e.WebAppScreenshots)
                    .WithOne(e => e.WebApp)
                    .HasForeignKey(e => e.WebAppId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.DeploymentTargets)
                    .WithOne(e => e.WebApp)
                    .HasForeignKey(e => e.WebAppId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AppDeploymentTarget>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Slug).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Status)
                    .IsRequired()
                    .HasMaxLength(40)
                    .HasConversion<string>();
                entity.Property(e => e.LastCleanupError).HasMaxLength(1000);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                entity.Property(e => e.PrivilegedHostAccessApproved).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.PrivilegedHostAccessApprovedAtUtc);
                entity.Property(e => e.PrivilegedHostAccessApprovedByUserId);

                entity.HasIndex(e => new { e.WebAppId, e.Slug }).IsUnique();
                entity.HasIndex(e => new { e.WebAppId, e.DefaultConstraintKey }).IsUnique();
                entity.HasIndex(e => e.ExecutionRunnerId);
                entity.HasIndex(e => e.ActiveDeploymentOperationId);
                entity.HasIndex(e => e.ActiveDeploymentExecutionAttemptId);

                entity.HasOne(e => e.ExecutionRunner)
                    .WithMany(e => e.AppDeploymentTargets)
                    .HasForeignKey(e => e.ExecutionRunnerId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<DeploymentOperation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
                entity.Property(e => e.PackagePath).IsRequired().HasMaxLength(1024);
                entity.Property(e => e.TargetSlug).HasMaxLength(120);
                entity.Property(e => e.ExecutorInstanceId).HasMaxLength(120);
                entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
                entity.Property(e => e.ResultJson).HasColumnType("longtext");
                entity.Property(e => e.CreatedAtUtc).IsRequired();
                entity.HasIndex(e => new { e.WebAppId, e.CreatedAtUtc });
                entity.HasIndex(e => new { e.WebAppId, e.Id }).IsUnique();
                entity.HasOne<WebApp>()
                    .WithMany()
                    .HasForeignKey(e => e.WebAppId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MarketplaceCategory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(120);
                entity.Property(e => e.Slug).IsRequired().HasMaxLength(140);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.SortOrder).IsRequired().HasDefaultValue(0);
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();

                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.Slug).IsUnique();
                entity.HasIndex(e => new { e.IsActive, e.SortOrder });
            });

            modelBuilder.Entity<WebAppUser>(entity =>
            {
                entity.HasKey(e => new { e.WebAppId, e.UserId });
                entity.HasIndex(e => e.WebAppId, "IX_WebAppUser_WebAppId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.WebAppId).IsRequired();
            });

            modelBuilder.Entity<WebAppStar>(entity =>
            {
                entity.HasKey(e => new { e.WebAppId, e.UserId });
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.WebAppId);
            });

            modelBuilder.Entity<WebAppScreenshot>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ImageUrl).IsRequired().HasMaxLength(500);
                entity.Property(e => e.Caption).HasMaxLength(240);
                entity.Property(e => e.SortOrder).IsRequired().HasDefaultValue(0);
                entity.Property(e => e.CreatedAt).IsRequired();

                entity.HasIndex(e => e.WebAppId);
                entity.HasIndex(e => new { e.WebAppId, e.SortOrder });
            });

            modelBuilder.Entity<WebAppStep>(entity =>
            {
                entity.HasKey(e => new { e.WebAppId, e.CustomStepId });
                entity.Property(e => e.LinkedAt).IsRequired();
                
                entity.HasIndex(e => e.WebAppId);
                entity.HasIndex(e => e.CustomStepId);
                
                entity.HasOne(e => e.WebApp)
                    .WithMany()
                    .HasForeignKey(e => e.WebAppId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.CustomStep)
                    .WithMany()
                    .HasForeignKey(e => e.CustomStepId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.LinkedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.LinkedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AppVersion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.AppId).IsRequired();
                entity.Property(e => e.VersionNumber).IsRequired();
                entity.Property(e => e.Summary).IsRequired().HasMaxLength(4000);
                entity.Property(e => e.Trigger).IsRequired();
                entity.Property(e => e.Source).HasMaxLength(80);
                entity.Property(e => e.Reason).HasMaxLength(160);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.CreatedBy).IsRequired();
                entity.Property(e => e.AiModel).HasMaxLength(200);
                entity.Property(e => e.SnapshotPath).IsRequired().HasMaxLength(500);
                entity.Property(e => e.FilesChangeSummary).HasColumnType("json");

                entity.HasIndex(e => e.AppId);
                entity.HasIndex(e => new { e.AppId, e.VersionNumber }).IsUnique();
                entity.HasIndex(e => e.AiSessionId);
                entity.HasIndex(e => e.CreatedBy);

                entity.HasOne(e => e.WebApp)
                    .WithMany()
                    .HasForeignKey(e => e.AppId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AppVersionStep>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.AppVersionId).IsRequired();
                entity.Property(e => e.StepId).IsRequired();
                entity.Property(e => e.StepName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Action).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.CodeSnapshot).IsRequired();
                entity.Property(e => e.VariablesSnapshot).IsRequired().HasColumnType("json");

                entity.HasIndex(e => e.AppVersionId);
                entity.HasIndex(e => e.StepId);

                entity.HasOne(e => e.AppVersion)
                    .WithMany(p => p.Steps)
                    .HasForeignKey(e => e.AppVersionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Step)
                    .WithMany()
                    .HasForeignKey(e => e.StepId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(e => new { e.Id });
                entity.Property(s => s.Token).IsRequired();
                entity.Property(s => s.RevokedAt).IsRequired(false);
                entity.Property(s => s.CreatedAt)
                    .IsRequired()
                    .ValueGeneratedOnAdd()
                    .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
                entity.Property(s => s.Expires).IsRequired();
            });

            modelBuilder.Entity<RecentActivity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ActivityType).IsRequired();
                entity.Property(e => e.EntityName).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Description).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasIndex(e => e.CreatedAt);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            });

            modelBuilder.Entity<ApiKey>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.HashedKey).IsRequired().HasMaxLength(500);
                entity.Property(e => e.KeyPrefix).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                
                // Configure Scope property with sentinel value to avoid EF Core warning
                entity.Property(e => e.Scope)
                    .IsRequired()
                    .HasDefaultValue(ApiKeyScope.Runner)
                    .HasSentinel((ApiKeyScope)0); // Use 0 as sentinel - if value is 0, use database default

                entity.Property(e => e.Source)
                    .IsRequired()
                    .HasDefaultValue(ApiKeySource.Manual);
                
                entity.Property(e => e.Description).HasMaxLength(500);

                entity.HasIndex(e => e.HashedKey).IsUnique();
                entity.HasIndex(e => e.LastUsedAt);
                entity.HasIndex(e => e.UsedAt);
            });
            
            modelBuilder.Entity<ApiKeyUser>(entity =>
            {
                entity.HasKey(e => new { e.ApiKeyId, e.UserId });
                entity.HasIndex(e => e.ApiKeyId, "IX_ApiKeyUser_ApiKeyId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.ApiKeyId).IsRequired();
            });

            modelBuilder.Entity<CliAuthorizationRequest>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserCode).IsRequired().HasMaxLength(32);
                entity.Property(e => e.HashedVerifier).IsRequired().HasMaxLength(128);
                entity.Property(e => e.DeviceName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.RemoteIp).HasMaxLength(64);
                entity.Property(e => e.EncryptedApiKey).HasMaxLength(1000);
                entity.HasIndex(e => e.ApiKeyId);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.ExpiresAt).IsRequired();
                entity.HasIndex(e => e.UserCode).IsUnique();
                entity.HasIndex(e => e.ExpiresAt);
            });

            modelBuilder.Entity<AiApiKey>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ProviderName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.AiProviderId).IsRequired(false);
                entity.Property(e => e.EncryptedApiKey).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.Description).HasMaxLength(500);

                // Optional FK relationship to AiProvider for referential integrity
                entity.HasOne(e => e.AiProvider)
                    .WithMany()
                    .HasForeignKey(e => e.AiProviderId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.ProviderName);
                entity.HasIndex(e => e.LastUsedAt);
            });

            modelBuilder.Entity<AiProvider>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.BaseUrl).HasMaxLength(200);
                entity.Property(e => e.EnvironmentKeyName).HasMaxLength(100);
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.RequiresApiKey).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.SortOrder).IsRequired().HasDefaultValue(0);
                entity.Property(e => e.CreatedAt).IsRequired();

                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.SortOrder);
            });

            modelBuilder.Entity<AiModel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.AiProviderId).IsRequired();
                entity.Property(e => e.ModelId).IsRequired().HasMaxLength(200);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(300);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.IsDefault).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.MaxTokens).IsRequired().HasDefaultValue(4096);
                entity.Property(e => e.SupportsStreaming).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.SupportsTools).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.SupportsVision).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.SortOrder).IsRequired().HasDefaultValue(0);
                entity.Property(e => e.CreatedAt).IsRequired();

                entity.HasOne(e => e.Provider)
                    .WithMany(p => p.Models)
                    .HasForeignKey(e => e.AiProviderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.AiProviderId);
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.IsDefault);
                entity.HasIndex(e => new { e.AiProviderId, e.ModelId }).IsUnique();
            });

            modelBuilder.Entity<SubscriptionPlan>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.PriceUsd).IsRequired().HasColumnType("decimal(10,2)");
                entity.Property(e => e.StripeProductId).HasMaxLength(100);
                entity.Property(e => e.StripePriceId).HasMaxLength(100);
                entity.Property(e => e.IsDefault).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.CapabilitiesJson).IsRequired().HasColumnType("longtext");
                entity.Property(e => e.SortOrder).IsRequired().HasDefaultValue(0);
                entity.Property(e => e.CreatedAt).IsRequired();

                entity.HasIndex(e => e.IsDefault);
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.SortOrder);
            });

            modelBuilder.Entity<FlowExecutionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FlowExecutionId).IsRequired();
                entity.Property(e => e.LogsJson).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();

                entity.HasOne(e => e.FlowExecution)
                    .WithMany()
                    .HasForeignKey(e => e.FlowExecutionId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Unique index to ensure one log entry per execution
                entity.HasIndex(e => e.FlowExecutionId).IsUnique();
            });

            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasKey(e => e.Key);
                entity.Property(e => e.Key).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Value).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500);
                
                entity.HasIndex(e => e.Key).IsUnique();
            });
            
            // Sensitive Variables Configuration
            modelBuilder.Entity<SensitiveVariable>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.EncryptedValue).IsRequired().HasMaxLength(2000);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.ResourceType).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ResourceId).IsRequired();
                entity.Property(e => e.VariableScope).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);

                entity.HasIndex(e => new { e.ResourceType, e.ResourceId });
                entity.HasIndex(e => e.IsActive);
                entity.HasIndex(e => e.LastUsedAt);
            });
            
            modelBuilder.Entity<SensitiveVariableUser>(entity =>
            {
                entity.HasKey(e => new { e.SensitiveVariableId, e.UserId });
                entity.HasIndex(e => e.SensitiveVariableId, "IX_SensitiveVariableUser_SensitiveVariableId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.SensitiveVariableId).IsRequired();
            });
            
            // Project/Organization entity configurations
            modelBuilder.Entity<Project>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(1000);
                entity.Property(e => e.OwnerId).IsRequired();
                entity.Property(e => e.Created).IsRequired();
                entity.Property(e => e.Updated).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                
                // Name must be unique across all projects
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasIndex(e => e.OwnerId);
                entity.HasIndex(e => new { e.OwnerId, e.IsActive });
                
                // Organization relationship (nullable for personal projects)
                entity.HasIndex(e => e.OrganizationId);
                entity.HasIndex(e => new { e.OrganizationId, e.IsActive });
                
                // Configure relationship with Owner
                entity.HasOne(e => e.Owner)
                    .WithMany(u => u.OwnedProjects)
                    .HasForeignKey(e => e.OwnerId)
                    .OnDelete(DeleteBehavior.Restrict); // Don't cascade delete projects when user is deleted
                    
                // Configure relationship with Organization (optional)
                entity.HasOne(e => e.Organization)
                    .WithMany(o => o.Projects)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired(false);
            });
            
            modelBuilder.Entity<ProjectMember>(entity =>
            {
                entity.HasKey(e => new { e.ProjectId, e.UserId });
                entity.Property(e => e.Role).IsRequired();
                entity.Property(e => e.JoinedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                
                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => new { e.ProjectId, e.UserId, e.IsActive });
                
                // Configure relationships
                entity.HasOne(e => e.Project)
                    .WithMany(p => p.Members)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.User)
                    .WithMany(u => u.ProjectMemberships)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.Inviter)
                    .WithMany()
                    .HasForeignKey(e => e.InvitedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            modelBuilder.Entity<ProjectApp>(entity =>
            {
                entity.HasKey(e => new { e.ProjectId, e.WebAppId });
                entity.Property(e => e.AddedAt).IsRequired();
                
                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => e.WebAppId);
                
                entity.HasOne(e => e.Project)
                    .WithMany(p => p.ProjectApps)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.WebApp)
                    .WithMany(a => a.ProjectApps)
                    .HasForeignKey(e => e.WebAppId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.AddedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.AddedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            modelBuilder.Entity<ProjectFlow>(entity =>
            {
                entity.HasKey(e => new { e.ProjectId, e.FlowId });
                entity.Property(e => e.AddedAt).IsRequired();
                
                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => e.FlowId);
                
                entity.HasOne(e => e.Project)
                    .WithMany(p => p.ProjectFlows)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.Flow)
                    .WithMany()
                    .HasForeignKey(e => e.FlowId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.AddedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.AddedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            modelBuilder.Entity<ProjectRunner>(entity =>
            {
                entity.HasKey(e => new { e.ProjectId, e.RunnerId });
                entity.Property(e => e.AddedAt).IsRequired();
                
                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => e.RunnerId);
                
                entity.HasOne(e => e.Project)
                    .WithMany(p => p.ProjectRunners)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.Runner)
                    .WithMany(r => r.ProjectRunners)
                    .HasForeignKey(e => e.RunnerId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.AddedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.AddedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            modelBuilder.Entity<ProjectCustomStep>(entity =>
            {
                entity.HasKey(e => new { e.ProjectId, e.CustomStepId });
                entity.Property(e => e.AddedAt).IsRequired();

                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => e.CustomStepId);

                entity.HasOne(e => e.Project)
                    .WithMany(p => p.ProjectCustomSteps)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.CustomStep)
                    .WithMany()
                    .HasForeignKey(e => e.CustomStepId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.AddedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.AddedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            modelBuilder.Entity<ProjectApiKey>(entity =>
            {
                entity.HasKey(e => new { e.ProjectId, e.ApiKeyId });
                entity.Property(e => e.AddedAt).IsRequired();
                entity.Property(e => e.AddedBy).IsRequired(false);

                entity.HasIndex(e => e.AddedBy);
                entity.HasIndex(e => e.ApiKeyId);
                entity.HasIndex(e => e.ProjectId);

                // Explicit relationships to avoid duplicate/shadow foreign keys
                entity.HasOne(e => e.Project)
                    .WithMany(p => p.ProjectApiKeys)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                entity.HasOne(e => e.ApiKey)
                    .WithMany(a => a.ProjectApiKeys)
                    .HasForeignKey(e => e.ApiKeyId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                entity.HasOne(e => e.AddedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.AddedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.ToTable("ProjectApiKeys");
            });
            
            modelBuilder.Entity<ProjectAiApiKey>(entity =>
            {
                entity.HasKey(e => new { e.ProjectId, e.AiApiKeyId });
                entity.Property(e => e.AddedAt).IsRequired();
                entity.Property(e => e.AddedBy).IsRequired(false);

                entity.HasIndex(e => e.AddedBy);
                entity.HasIndex(e => e.AiApiKeyId);
                entity.HasIndex(e => e.ProjectId);

                // Explicit relationships to avoid duplicate/shadow foreign keys
                entity.HasOne(e => e.Project)
                    .WithMany(p => p.ProjectAiApiKeys)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                entity.HasOne(e => e.AiApiKey)
                    .WithMany(a => a.ProjectAiApiKeys)
                    .HasForeignKey(e => e.AiApiKeyId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                entity.HasOne(e => e.AddedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.AddedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.ToTable("ProjectAiApiKeys");
            });

            // Invitation tokens
            modelBuilder.Entity<InvitationToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Token).IsRequired();
                entity.Property(e => e.Email).IsRequired();
                entity.Property(e => e.ProjectId).IsRequired();
                entity.Property(e => e.InvitedBy).IsRequired();
                entity.Property(e => e.ExpiresAt).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();

                entity.HasIndex(e => e.Token).IsUnique();
                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => e.Email);
            });

            // AI Chat History
            modelBuilder.Entity<AiChatSession>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ResourceType).IsRequired();
                entity.Property(e => e.ResourceId).IsRequired();
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Provider).HasMaxLength(50);
                entity.Property(e => e.Model).HasMaxLength(100);
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();

                // Indexes for efficient querying
                entity.HasIndex(e => new { e.ResourceType, e.ResourceId });
                entity.HasIndex(e => new { e.ResourceType, e.ResourceId, e.IsActive });
                entity.HasIndex(e => e.CreatedAt);
            });

            modelBuilder.Entity<AiChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.SessionId).IsRequired();
                entity.Property(e => e.Role).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.MessageType).IsRequired();
                entity.Property(e => e.Metadata).HasColumnType("json");
                entity.Property(e => e.CreatedAt).IsRequired();

                // Configure relationship
                entity.HasOne(e => e.Session)
                    .WithMany(s => s.Messages)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Indexes for efficient querying
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.CreatedAt);
            });

            modelBuilder.Entity<AiChatSessionUser>(entity =>
            {
                entity.HasKey(e => new { e.SessionId, e.UserId });
                entity.HasIndex(e => e.SessionId, "IX_AiChatSessionUser_SessionId");
                entity.HasIndex(e => e.UserId, "IX_AiChatSessionUser_UserId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.SessionId).IsRequired();

                // Configure relationships
                entity.HasOne(e => e.Session)
                    .WithMany(s => s.AiChatSessionUsers)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AiGuidanceDocument>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Key).IsRequired().HasMaxLength(120);
                  entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                  entity.Property(e => e.Surface).IsRequired().HasMaxLength(50);
                  entity.Property(e => e.DocumentKind).IsRequired().HasMaxLength(50);
                  entity.Property(e => e.Description).HasMaxLength(500);
                  entity.Property(e => e.TagsJson).HasMaxLength(4000);
                  entity.Property(e => e.OutputPath).HasMaxLength(500);
                  entity.Property(e => e.ContentMarkdown).IsRequired();
                  entity.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);
                  entity.Property(e => e.RequiredByDefault).IsRequired().HasDefaultValue(false);
                  entity.Property(e => e.IsModelLoadable).IsRequired().HasDefaultValue(true);
                  entity.Property(e => e.SortOrder).IsRequired().HasDefaultValue(100);
                  entity.Property(e => e.Version).IsRequired().HasDefaultValue(1);
                  entity.Property(e => e.Source).IsRequired().HasMaxLength(20).HasDefaultValue("Admin");
                  entity.Property(e => e.SeedContentHash).HasMaxLength(64);
                  entity.Property(e => e.IsSeedRetired).IsRequired().HasDefaultValue(false);
                  entity.Property(e => e.CreatedAt).IsRequired();
  
                  entity.HasIndex(e => e.Key).IsUnique();
                  entity.HasIndex(e => new { e.Surface, e.DocumentKind });
                  entity.HasIndex(e => e.IsEnabled);
              });

            // User Settings
            modelBuilder.Entity<UserSetting>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.SettingKey).IsRequired().HasMaxLength(100);
                entity.Property(e => e.SettingValue).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();

                // Unique constraint: one setting per user per key
                entity.HasIndex(e => new { e.UserId, e.SettingKey }).IsUnique();
                entity.HasIndex(e => e.UserId);
            });
            
            // =====================================================
            // Organization Entity Configurations
            // =====================================================
            
            modelBuilder.Entity<Organization>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Description).HasMaxLength(2000);
                entity.Property(e => e.Slug).HasMaxLength(100);
                entity.Property(e => e.OwnerId).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.AllowPublicSignup).IsRequired().HasDefaultValue(false);
                entity.Property(e => e.RequireEmailDomain).HasMaxLength(255);
                entity.Property(e => e.LogoUrl).HasMaxLength(500);
                entity.Property(e => e.PrimaryColor).HasMaxLength(7);
                
                // Slug must be unique
                entity.HasIndex(e => e.Slug).IsUnique();
                entity.HasIndex(e => e.OwnerId);
                entity.HasIndex(e => new { e.OwnerId, e.IsActive });
                
                // Configure relationship with Owner
                entity.HasOne(e => e.Owner)
                    .WithMany(u => u.OwnedOrganizations)
                    .HasForeignKey(e => e.OwnerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            modelBuilder.Entity<OrganizationMember>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrganizationId).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.Role).IsRequired();
                entity.Property(e => e.JoinedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                
                // Unique constraint: one membership per user per organization
                entity.HasIndex(e => new { e.OrganizationId, e.UserId }).IsUnique();
                entity.HasIndex(e => e.OrganizationId);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => new { e.OrganizationId, e.UserId, e.IsActive });
                
                // Configure relationships
                entity.HasOne(e => e.Organization)
                    .WithMany(o => o.Members)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.User)
                    .WithMany(u => u.OrganizationMemberships)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.Inviter)
                    .WithMany()
                    .HasForeignKey(e => e.InvitedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            modelBuilder.Entity<OrganizationAppAssignment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrganizationId).IsRequired();
                entity.Property(e => e.AppId).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.AssignedAt).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                
                // Unique constraint: one assignment per user per app per organization
                entity.HasIndex(e => new { e.OrganizationId, e.AppId, e.UserId }).IsUnique();
                entity.HasIndex(e => e.OrganizationId);
                entity.HasIndex(e => e.AppId);
                entity.HasIndex(e => e.UserId);
                
                // Configure relationships
                entity.HasOne(e => e.Organization)
                    .WithMany(o => o.AppAssignments)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.App)
                    .WithMany()
                    .HasForeignKey(e => e.AppId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.Assigner)
                    .WithMany()
                    .HasForeignKey(e => e.AssignedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });
            
            modelBuilder.Entity<OrganizationInvitation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OrganizationId).IsRequired();
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Role).IsRequired();
                entity.Property(e => e.Token).IsRequired().HasMaxLength(64);
                entity.Property(e => e.InvitedBy).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.ExpiresAt).IsRequired();
                entity.Property(e => e.IsConsumed).IsRequired().HasDefaultValue(false);
                
                // Token must be unique
                entity.HasIndex(e => e.Token).IsUnique();
                entity.HasIndex(e => e.OrganizationId);
                entity.HasIndex(e => e.Email);
                entity.HasIndex(e => new { e.OrganizationId, e.Email, e.IsConsumed });
                
                // Configure relationships
                entity.HasOne(e => e.Organization)
                    .WithMany(o => o.Invitations)
                    .HasForeignKey(e => e.OrganizationId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                entity.HasOne(e => e.Inviter)
                    .WithMany()
                    .HasForeignKey(e => e.InvitedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

        }

        private static void ConfigureJson<T>(PropertyBuilder<T> property)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            property.HasColumnType("json").HasConversion(
                value => JsonSerializer.Serialize(value, options),
                json => JsonSerializer.Deserialize<T>(json, options)!);
            property.Metadata.SetValueComparer(new ValueComparer<T>(
                (left, right) => JsonSerializer.Serialize(left, options) == JsonSerializer.Serialize(right, options),
                value => JsonSerializer.Serialize(value, options).GetHashCode(),
                value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)!));
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            PrepareDeploymentTargets();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default)
        {
            await PrepareDeploymentTargetsAsync(cancellationToken);
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void PrepareDeploymentTargets()
        {
            foreach (var appEntry in ChangeTracker.Entries<WebApp>()
                         .Where(entry => entry.State == EntityState.Added))
            {
                var app = appEntry.Entity;
                if (!app.DeploymentTargets.Any(target => target.IsDefault))
                {
                    app.DeploymentTargets.Add(AppDeploymentTarget.CreateLive(app.Id));
                }
            }

            foreach (var targetEntry in ChangeTracker.Entries<AppDeploymentTarget>()
                         .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                targetEntry.Entity.SynchronizeDefaultConstraint();
            }

            ValidateDefaultTargetCounts();
        }

        private async Task PrepareDeploymentTargetsAsync(CancellationToken cancellationToken)
        {
            foreach (var appEntry in ChangeTracker.Entries<WebApp>()
                         .Where(entry => entry.State == EntityState.Added))
            {
                var app = appEntry.Entity;
                if (!app.DeploymentTargets.Any(target => target.IsDefault))
                {
                    app.DeploymentTargets.Add(AppDeploymentTarget.CreateLive(app.Id));
                }
            }

            foreach (var targetEntry in ChangeTracker.Entries<AppDeploymentTarget>()
                         .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                targetEntry.Entity.SynchronizeDefaultConstraint();
            }

            await ValidateDefaultTargetCountsAsync(cancellationToken);
        }

        private void ValidateDefaultTargetCounts()
        {
            var targetEntries = ChangeTracker.Entries<AppDeploymentTarget>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToArray();

            foreach (var appId in targetEntries.Select(entry => entry.Entity.WebAppId).Distinct())
            {
                var appIsBeingDeleted = ChangeTracker.Entries<WebApp>().Any(entry =>
                    entry.Entity.Id == appId &&
                    entry.State == EntityState.Deleted);
                if (appIsBeingDeleted)
                {
                    continue;
                }

                var defaultCount = AppDeploymentTargets
                    .AsNoTracking()
                    .Count(target => target.WebAppId == appId && target.IsDefault);

                foreach (var entry in targetEntries.Where(entry => entry.Entity.WebAppId == appId))
                {
                    var wasDefault = entry.State == EntityState.Added
                        ? false
                        : entry.OriginalValues.GetValue<bool>(nameof(AppDeploymentTarget.IsDefault));
                    var isDefault = entry.State == EntityState.Deleted
                        ? false
                        : entry.Entity.IsDefault;

                    defaultCount += (isDefault ? 1 : 0) - (wasDefault ? 1 : 0);
                }

                if (defaultCount != 1)
                {
                    throw new InvalidOperationException(
                        "Every App must have exactly one primary deployment target.");
                }
            }
        }

        private async Task ValidateDefaultTargetCountsAsync(CancellationToken cancellationToken)
        {
            var targetEntries = ChangeTracker.Entries<AppDeploymentTarget>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToArray();

            foreach (var appId in targetEntries.Select(entry => entry.Entity.WebAppId).Distinct())
            {
                var appIsBeingDeleted = ChangeTracker.Entries<WebApp>().Any(entry =>
                    entry.Entity.Id == appId &&
                    entry.State == EntityState.Deleted);
                if (appIsBeingDeleted)
                {
                    continue;
                }

                var defaultCount = await AppDeploymentTargets
                    .AsNoTracking()
                    .CountAsync(
                        target => target.WebAppId == appId && target.IsDefault,
                        cancellationToken);

                foreach (var entry in targetEntries.Where(entry => entry.Entity.WebAppId == appId))
                {
                    var wasDefault = entry.State == EntityState.Added
                        ? false
                        : entry.OriginalValues.GetValue<bool>(nameof(AppDeploymentTarget.IsDefault));
                    var isDefault = entry.State == EntityState.Deleted
                        ? false
                        : entry.Entity.IsDefault;

                    defaultCount += (isDefault ? 1 : 0) - (wasDefault ? 1 : 0);
                }

                if (defaultCount != 1)
                {
                    throw new InvalidOperationException(
                        "Every App must have exactly one primary deployment target.");
                }
            }
        }

        public void BeginTransaction()
        {
            _transaction = Database.BeginTransaction();
        }

        public int Commit()
        {
            try
            {
                var saveChanges = SaveChanges();
                _transaction.Commit();
                return saveChanges;
            }
            finally
            {
                _transaction.Dispose();
            }
        }

        public void Rollback()
        {
            _transaction.Rollback();
            _transaction.Dispose();
        }

        public Task<int> CommitAsync()
        {
            try
            {
                var saveChangesAsync = SaveChangesAsync();
                _transaction.Commit();
                return saveChangesAsync;
            }
            finally
            {
                _transaction.Dispose();
            }
        }
    }
}
