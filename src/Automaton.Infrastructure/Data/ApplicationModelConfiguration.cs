using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class ApplicationModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureMarketplaceCategory(modelBuilder);
        ConfigureWebAppUser(modelBuilder);
        ConfigureWebAppStar(modelBuilder);
        ConfigureWebAppScreenshot(modelBuilder);
        ConfigureWebAppStep(modelBuilder);
        ConfigureAppVersion(modelBuilder);
        ConfigureAppVersionStep(modelBuilder);
        ConfigureRefreshToken(modelBuilder);
        ConfigureRecentActivity(modelBuilder);
    }

    private static void ConfigureMarketplaceCategory(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureWebAppUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<WebAppUser>(entity =>
            {
                entity.HasKey(e => new { e.WebAppId, e.UserId });
                entity.HasIndex(e => e.WebAppId, "IX_WebAppUser_WebAppId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.WebAppId).IsRequired();
            });
    }

    private static void ConfigureWebAppStar(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<WebAppStar>(entity =>
            {
                entity.HasKey(e => new { e.WebAppId, e.UserId });
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.WebAppId);
            });
    }

    private static void ConfigureWebAppScreenshot(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureWebAppStep(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAppVersion(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAppVersionStep(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureRefreshToken(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureRecentActivity(ModelBuilder modelBuilder)
    {
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
    }
}
