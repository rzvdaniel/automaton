using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class RunnerModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureRunner(modelBuilder);
        ConfigureSelfHostedRunnerEnrollment(modelBuilder);
        ConfigureRunnerConnectionSession(modelBuilder);
        ConfigureRunnerUser(modelBuilder);
        ConfigureRunnerHost(modelBuilder);
        ConfigureRunnerHostProvisioningOperation(modelBuilder);
        ConfigureInfrastructureRunnerUpdateOperation(modelBuilder);
        ConfigureRunnerEnrollment(modelBuilder);
        ConfigureSecretDefinition(modelBuilder);
        ConfigureSecretVersion(modelBuilder);
    }

    private static void ConfigureRunner(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureSelfHostedRunnerEnrollment(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureRunnerConnectionSession(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureRunnerUser(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<RunnerUser>(entity =>
            {
                entity.HasKey(e => new { e.RunnerId, e.UserId });
                entity.HasIndex(e => e.RunnerId, "IX_RunnerUser_RunnerId");
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.RunnerId).IsRequired();
            });
    }

    private static void ConfigureRunnerHost(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureRunnerHostProvisioningOperation(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureInfrastructureRunnerUpdateOperation(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureRunnerEnrollment(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureSecretDefinition(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<SecretDefinition>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OwnerType).IsRequired().HasMaxLength(40);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
                entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.Name }).IsUnique();
                entity.HasIndex(e => new { e.OwnerType, e.OwnerId, e.DeletedAtUtc });
            });
    }

    private static void ConfigureSecretVersion(ModelBuilder modelBuilder)
    {
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
    }
}
