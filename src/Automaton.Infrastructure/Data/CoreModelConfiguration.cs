using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class CoreModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureApplicationUserRole(modelBuilder);
        ConfigureApplicationUser(modelBuilder);
        ConfigureActionSchedule(modelBuilder);
        ConfigureActionScheduleOccurrence(modelBuilder);
        ConfigureFlow(modelBuilder);
        ConfigureFlowUser(modelBuilder);
        ConfigureFlowExecution(modelBuilder);
        ConfigureAuditEvent(modelBuilder);
        ConfigureApplicationEvent(modelBuilder);
        ConfigureAppModuleExecution(modelBuilder);
        ConfigureFlowExecutionUser(modelBuilder);
    }

    private static void ConfigureApplicationUserRole(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureApplicationUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.HasOne(e => e.SubscriptionPlan)
                    .WithMany()
                    .HasForeignKey(e => e.SubscriptionPlanId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.SubscriptionPlanId);
            });
    }

    private static void ConfigureActionSchedule(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureActionScheduleOccurrence(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureFlow(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<Flow>(entity =>
            {
                entity.HasKey(e => new { e.Id });
            });
    }

    private static void ConfigureFlowUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<FlowUser>(entity =>
            {
                entity.HasKey(e => new { e.FlowId, e.UserId });
                entity.HasIndex(e => e.FlowId, "IX_FlowUser_FlowId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.FlowId).IsRequired();
            });
    }

    private static void ConfigureFlowExecution(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<FlowExecution>(entity =>
            {
                entity.HasKey(e => new { e.Id });
            });
    }

    private static void ConfigureAuditEvent(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureApplicationEvent(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAppModuleExecution(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureFlowExecutionUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<FlowExecutionUser>(entity =>
            {
                entity.HasKey(e => new { e.FlowExecutionId, e.UserId });
                entity.HasIndex(e => e.FlowExecutionId, "IX_FlowExecutionUser_FlowExecutionId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.FlowExecutionId).IsRequired();
            });
    }
}
