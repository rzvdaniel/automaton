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
        public virtual DbSet<Project> Projects { get; set; }
        public virtual DbSet<UserProjectRole> UserProjectRoles { get; set; }
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
        public virtual DbSet<AiApiKey> AiApiKeys { get; set; }
        public virtual DbSet<WebApp> WebApps { get; set; }
        public virtual DbSet<WebAppUser> WebAppUsers { get; set; }
        public virtual DbSet<Setting> Settings { get; set; }

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
                
                entity.HasOne(e => e.Project)
                    .WithMany()
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.ProjectId);
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
                
                entity.HasOne(e => e.Project)
                    .WithMany()
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.ProjectId);
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
                
                entity.HasOne(e => e.Project)
                    .WithMany()
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.ProjectId);
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
                
                entity.Property(e => e.PublishingScope)
                    .IsRequired()
                    .HasDefaultValue(PublishingScope.Anonymous);

                entity.HasOne(e => e.Project)
                    .WithMany()
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.ProjectId);
            });

            modelBuilder.Entity<Entities.WebAppUser>(entity =>
            {
                entity.HasKey(e => new { e.WebAppId, e.UserId });
                entity.HasIndex(e => e.WebAppId, "IX_WebAppUser_WebAppId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.WebAppId).IsRequired();
            });

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasKey(e => new { e.Id });
                entity.Property(s => s.Token).IsRequired();
                entity.Property(s => s.RevokedAt).IsRequired(false);
                entity.Property(s => s.CreatedAt).IsRequired().HasDefaultValue(DateTime.Now);
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
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                
                // Configure Scope property with sentinel value to avoid EF Core warning
                entity.Property(e => e.Scope)
                    .IsRequired()
                    .HasDefaultValue(ApiKeyScope.Runner)
                    .HasSentinel((ApiKeyScope)0); // Use 0 as sentinel - if value is 0, use database default
                
                entity.Property(e => e.Description).HasMaxLength(500);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.HashedKey).IsUnique();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => new { e.UserId, e.IsActive });
                entity.HasIndex(e => e.LastUsedAt);
            });

            modelBuilder.Entity<AiApiKey>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Provider).IsRequired();
                entity.Property(e => e.EncryptedApiKey).IsRequired().HasMaxLength(1000);
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(e => e.Description).HasMaxLength(500);

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => new { e.UserId, e.IsActive });
                entity.HasIndex(e => e.LastUsedAt);
                entity.HasIndex(e => new { e.UserId, e.Provider });
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

            // Configure Project entity
            modelBuilder.Entity<Project>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.AdminUserId).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();

                entity.HasOne(e => e.AdminUser)
                    .WithMany()
                    .HasForeignKey(e => e.AdminUserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.AdminUserId);
                entity.HasIndex(e => e.CreatedAt);
            });

            // Configure UserProjectRole entity
            modelBuilder.Entity<UserProjectRole>(entity =>
            {
                entity.HasKey(e => new { e.UserId, e.ProjectId });
                entity.Property(e => e.Role).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();

                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Project)
                    .WithMany(p => p.UserProjectRoles)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.ProjectId);
                entity.HasIndex(e => new { e.UserId, e.ProjectId, e.Role });
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