using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class ProjectModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureProjectCustomStep(modelBuilder);
        ConfigureProjectApiKey(modelBuilder);
        ConfigureProjectAiApiKey(modelBuilder);
        ConfigureInvitationToken(modelBuilder);
        ConfigureAiChatSession(modelBuilder);
        ConfigureAiChatMessage(modelBuilder);
        ConfigureAiChatSessionUser(modelBuilder);
        ConfigureAiGuidanceDocument(modelBuilder);
        ConfigureUserSetting(modelBuilder);
    }

    private static void ConfigureProjectCustomStep(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureProjectApiKey(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureProjectAiApiKey(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureInvitationToken(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAiChatSession(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAiChatMessage(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAiChatSessionUser(ModelBuilder modelBuilder)
    {
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

    private static void ConfigureAiGuidanceDocument(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureUserSetting(ModelBuilder modelBuilder)
    {
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
    }
}
