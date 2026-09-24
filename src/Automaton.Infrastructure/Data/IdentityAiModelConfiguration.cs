using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class IdentityAiModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureApiKey(modelBuilder);
        ConfigureApiKeyUser(modelBuilder);
        ConfigureCliAuthorizationRequest(modelBuilder);
        ConfigureAiApiKey(modelBuilder);
        ConfigureAiProvider(modelBuilder);
        ConfigureAiModel(modelBuilder);
        ConfigureSubscriptionPlan(modelBuilder);
        ConfigureFlowExecutionLog(modelBuilder);
        ConfigureSetting(modelBuilder);
        ConfigureSensitiveVariable(modelBuilder);
        ConfigureSensitiveVariableUser(modelBuilder);
        ConfigureProject(modelBuilder);
        ConfigureProjectMember(modelBuilder);
        ConfigureProjectApp(modelBuilder);
        ConfigureProjectFlow(modelBuilder);
        ConfigureProjectRunner(modelBuilder);
    }

    private static void ConfigureApiKey(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureApiKeyUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<ApiKeyUser>(entity =>
            {
                entity.HasKey(e => new { e.ApiKeyId, e.UserId });
                entity.HasIndex(e => e.ApiKeyId, "IX_ApiKeyUser_ApiKeyId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.ApiKeyId).IsRequired();
            });
    }

    private static void ConfigureCliAuthorizationRequest(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAiApiKey(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAiProvider(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureAiModel(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureSubscriptionPlan(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureFlowExecutionLog(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureSetting(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<Setting>(entity =>
            {
                entity.HasKey(e => e.Key);
                entity.Property(e => e.Key).IsRequired().HasMaxLength(255);
                entity.Property(e => e.Value).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500);

                entity.HasIndex(e => e.Key).IsUnique();
            });
    }

    private static void ConfigureSensitiveVariable(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureSensitiveVariableUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<SensitiveVariableUser>(entity =>
            {
                entity.HasKey(e => new { e.SensitiveVariableId, e.UserId });
                entity.HasIndex(e => e.SensitiveVariableId, "IX_SensitiveVariableUser_SensitiveVariableId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.SensitiveVariableId).IsRequired();
            });
    }

    private static void ConfigureProject(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureProjectMember(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureProjectApp(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureProjectFlow(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureProjectRunner(ModelBuilder modelBuilder)
    {
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
    }
}
