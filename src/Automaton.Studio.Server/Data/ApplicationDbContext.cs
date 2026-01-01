using Automaton.Core.Enums;
using Automaton.Studio.Server.Entities;
using Common.Authentication;
using Common.EF;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Automaton.Studio.Server.Data
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
        public virtual DbSet<Runner> Runners { get; set; }
        public virtual DbSet<RunnerUser> RunnerUsers { get; set; }
        public virtual DbSet<CustomStep> CustomSteps { get; set; }
        public virtual DbSet<CustomStepUser> CustomStepUsers { get; set; }
        public virtual DbSet<Schedule> Schedules { get; set; }
        public virtual DbSet<ScheduleUser> ScheduleUsers { get; set; }
        public virtual DbSet<Log> Logs { get; set; }
        public virtual DbSet<RecentActivity> RecentActivities { get; set; }
        public virtual DbSet<ApiKey> ApiKeys { get; set; }
        public virtual DbSet<ApiKeyUser> ApiKeyUsers { get; set; }
        public virtual DbSet<AiApiKey> AiApiKeys { get; set; }
        public virtual DbSet<AiApiKeyUser> AiApiKeyUsers { get; set; }
        public virtual DbSet<AiProvider> AiProviders { get; set; }
        public virtual DbSet<AiModel> AiModels { get; set; }
        public virtual DbSet<WebApp> WebApps { get; set; }
        public virtual DbSet<WebAppUser> WebAppUsers { get; set; }
        public virtual DbSet<WebAppStep> WebAppSteps { get; set; }
        public virtual DbSet<Setting> Settings { get; set; }
        public virtual DbSet<SensitiveVariable> SensitiveVariables { get; set; }
        public virtual DbSet<SensitiveVariableUser> SensitiveVariableUsers { get; set; }
        
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

        // AI Chat History entities
        public virtual DbSet<AiChatSession> AiChatSessions { get; set; }
        public virtual DbSet<AiChatMessage> AiChatMessages { get; set; }
        public virtual DbSet<AiChatSessionUser> AiChatSessionUsers { get; set; }

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
            });

            modelBuilder.Entity<RunnerUser>(entity =>
            {
                entity.HasKey(e => new { e.RunnerId, e.UserId });
                entity.HasIndex(e => e.RunnerId, "IX_RunnerUser_RunnerId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.RunnerId).IsRequired();
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

            modelBuilder.Entity<Entities.WebApp>(entity =>
            {
                entity.HasKey(e => new { e.Id });
                entity.HasIndex(e => e.PublicUrl).IsUnique();
            });

            modelBuilder.Entity<Entities.WebAppUser>(entity =>
            {
                entity.HasKey(e => new { e.WebAppId, e.UserId });
                entity.HasIndex(e => e.WebAppId, "IX_WebAppUser_WebAppId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.WebAppId).IsRequired();
            });

            modelBuilder.Entity<Entities.WebAppStep>(entity =>
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
                
                entity.Property(e => e.Description).HasMaxLength(500);

                entity.HasIndex(e => e.HashedKey).IsUnique();
                entity.HasIndex(e => e.LastUsedAt);
            });
            
            modelBuilder.Entity<ApiKeyUser>(entity =>
            {
                entity.HasKey(e => new { e.ApiKeyId, e.UserId });
                entity.HasIndex(e => e.ApiKeyId, "IX_ApiKeyUser_ApiKeyId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.ApiKeyId).IsRequired();
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
            
            modelBuilder.Entity<AiApiKeyUser>(entity =>
            {
                entity.HasKey(e => new { e.AiApiKeyId, e.UserId });
                entity.HasIndex(e => e.AiApiKeyId, "IX_AiApiKeyUser_AiApiKeyId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.AiApiKeyId).IsRequired();
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
                
                // Configure relationship with Owner
                entity.HasOne(e => e.Owner)
                    .WithMany(u => u.OwnedProjects)
                    .HasForeignKey(e => e.OwnerId)
                    .OnDelete(DeleteBehavior.Restrict); // Don't cascade delete projects when user is deleted
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
                    .WithMany()
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
            modelBuilder.Entity<Entities.InvitationToken>(entity =>
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