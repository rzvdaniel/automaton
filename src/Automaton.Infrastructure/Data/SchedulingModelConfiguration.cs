using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class SchedulingModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureScheduleUser(modelBuilder);
        ConfigureCustomStep(modelBuilder);
        ConfigureCustomStepUser(modelBuilder);
        ConfigureWebApp(modelBuilder);
        ConfigureAppDeploymentTarget(modelBuilder);
        ConfigureDeploymentOperation(modelBuilder);
    }

    private static void ConfigureScheduleUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<ScheduleUser>(entity =>
            {
                entity.HasKey(e => new { e.ScheduleId, e.UserId });
                entity.HasIndex(e => e.ScheduleId, "IX_ScheduleUser_ScheduleId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.ScheduleId).IsRequired();
            });
    }

    private static void ConfigureCustomStep(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<CustomStep>(entity =>
            {
                entity.HasKey(e => new { e.Id });
            });
    }

    private static void ConfigureCustomStepUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<CustomStepUser>(entity =>
            {
                entity.HasKey(e => new { e.CustomStepId, e.UserId });
                entity.HasIndex(e => e.CustomStepId, "IX_CustomStepUser_CustomStepId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.CustomStepId).IsRequired();
            });
    }

    private static void ConfigureWebApp(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAppDeploymentTarget(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureDeploymentOperation(ModelBuilder modelBuilder)
    {
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
    }
}
