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
        ConfigureAppShare(modelBuilder);
        ConfigureAppCustomDomain(modelBuilder);
        ConfigureWebAppStar(modelBuilder);
        ConfigureUserHome(modelBuilder);
        ConfigureWebAppScreenshot(modelBuilder);
        ConfigureAppVersion(modelBuilder);
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

    private static void ConfigureAppShare(ModelBuilder modelBuilder)
    {
        // A team share ends when the team is deleted.
        modelBuilder.Entity<WebApp>().HasOne<Project>().WithMany().HasForeignKey(e => e.SharedWithProjectId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<AppShare>(entity =>
        {
            entity.HasKey(e => new { e.WebAppId, e.UserId });
            entity.HasIndex(e => e.UserId);
            // A share goes away with the app or the person it names.
            entity.HasOne<WebApp>().WithMany().HasForeignKey(e => e.WebAppId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureAppCustomDomain(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppCustomDomain>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Host).IsRequired().HasMaxLength(253);
            entity.Property(e => e.VerificationToken).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.Host).IsUnique();
            entity.HasIndex(e => e.WebAppId);
            // A domain goes away with its app.
            entity.HasOne<WebApp>().WithMany().HasForeignKey(e => e.WebAppId).OnDelete(DeleteBehavior.Cascade);
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

    private static void ConfigureUserHome(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserHomePin>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.AppId });
            entity.HasIndex(e => new { e.UserId, e.SortOrder });
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<WebApp>().WithMany().HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserHomeDismissal>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.CardKey });
            entity.Property(e => e.CardKey).HasMaxLength(100);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserNotificationAck>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.Key });
            entity.Property(e => e.Key).HasMaxLength(100);
            entity.Property(e => e.Fingerprint).HasMaxLength(64);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AppHomeCard>(entity =>
        {
            entity.HasKey(e => e.AppId);
            entity.Property(e => e.CardJson).IsRequired().HasMaxLength(16384);
            entity.Property(e => e.InvalidReason).HasMaxLength(300);
            entity.HasOne<WebApp>().WithMany().HasForeignKey(e => e.AppId).OnDelete(DeleteBehavior.Cascade);
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
                entity.Property(e => e.FilesChangeSummary).HasColumnType("jsonb");

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
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
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
