using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Data;

internal static class RunnerStorageModelConfiguration
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureExecutionSecretGrant(modelBuilder);
        ConfigureInfrastructureWorkflowExecution(modelBuilder);
        ConfigureInfrastructureWorkflowExecutionLog(modelBuilder);
        ConfigureHostedRunner(modelBuilder);
        ConfigureAccountStorageAllocation(modelBuilder);
        ConfigureHostedRunnerStorageObservation(modelBuilder);
        ConfigureHostedRunnerStorageVerification(modelBuilder);
        ConfigureHostContainerExecutionOperation(modelBuilder);
        ConfigureHostedRunnerSpecification(modelBuilder);
        ConfigureHostedRunnerSpecificationRevision(modelBuilder);
        ConfigureHostedRunnerPlatformSettings(modelBuilder);
        ConfigureHostedRunnerDeployment(modelBuilder);
        ConfigureSchedule(modelBuilder);
    }

    private static void ConfigureExecutionSecretGrant(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureInfrastructureWorkflowExecution(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureInfrastructureWorkflowExecutionLog(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureHostedRunner(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<HostedRunner>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.StorageProjectId);
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
                ApplicationDbModelJson.ConfigureJson(entity.Property(e => e.SpecificationSnapshot));
                ApplicationDbModelJson.ConfigureJson(entity.Property(e => e.PendingSpecificationSnapshot));

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
    }

    private static void ConfigureAccountStorageAllocation(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<AccountStorageAllocation>(entity =>
            {
                entity.HasKey(e => e.AccountId);
                entity.Property(e => e.StorageProjectId).IsRequired();
                entity.Property(e => e.StorageFilesystemUuid).HasMaxLength(80);
                entity.ToTable(table => table.HasCheckConstraint(
                    "CK_AccountStorageAllocations_StorageProjectId_Positive",
                    "StorageProjectId > 0"));
                entity.HasIndex(e => e.StorageProjectId).IsUnique();
                entity.HasOne(e => e.Account)
                    .WithMany()
                    .HasForeignKey(e => e.AccountId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureHostedRunnerStorageObservation(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<HostedRunnerStorageObservation>(entity =>
            {
                entity.Property(e => e.Source).IsRequired().HasMaxLength(40);
                entity.Property(e => e.Error).HasMaxLength(2000);
                entity.Property(e => e.RunnerVersion).HasMaxLength(80);
                entity.HasIndex(e => new { e.HostedRunnerId, e.MeasuredAtUtc });
                entity.HasOne(e => e.HostedRunner).WithMany(e => e.StorageObservations)
                    .HasForeignKey(e => e.HostedRunnerId).OnDelete(DeleteBehavior.Cascade);
            });
    }

    private static void ConfigureHostedRunnerStorageVerification(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureHostContainerExecutionOperation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HostContainerExecutionOperation>(entity =>
        {
            entity.HasKey(operation => operation.Id);
            entity.Property(operation => operation.Status).IsRequired().HasMaxLength(32);
            entity.Property(operation => operation.RequestHash).IsRequired().HasMaxLength(64);
            entity.Property(operation => operation.ResultJson).HasColumnType("longtext");
            entity.HasIndex(operation => new { operation.InfrastructureRunnerId, operation.CreatedAtUtc });
            entity.HasIndex(operation => new { operation.HostedRunnerId, operation.CreatedAtUtc });
        });
    }

    private static void ConfigureHostedRunnerSpecification(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<HostedRunnerSpecification>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Code).IsRequired().HasMaxLength(80);
                entity.HasIndex(e => e.Code).IsUnique();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
                entity.Property(e => e.Description).HasMaxLength(2000);
                ApplicationDbModelJson.ConfigureJson(entity.Property(e => e.Resources));
            });
    }

    private static void ConfigureHostedRunnerSpecificationRevision(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<HostedRunnerSpecificationRevision>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(160);
                entity.Property(e => e.Description).HasMaxLength(2000);
                ApplicationDbModelJson.ConfigureJson(entity.Property(e => e.Resources));
                entity.HasIndex(e => new { e.SpecificationId, e.Revision }).IsUnique();
                entity.HasOne(e => e.Specification)
                    .WithMany(e => e.Revisions)
                    .HasForeignKey(e => e.SpecificationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
    }

    private static void ConfigureHostedRunnerPlatformSettings(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<HostedRunnerPlatformSettings>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.DefaultSpecification)
                    .WithMany()
                    .HasForeignKey(e => e.DefaultSpecificationId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
    }

    private static void ConfigureHostedRunnerDeployment(ModelBuilder modelBuilder)
    {
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
    }

    private static void ConfigureSchedule(ModelBuilder modelBuilder)
    {
            modelBuilder.Entity<Schedule>(entity =>
            {
                entity.HasKey(e => new { e.Id });
            });
    }
}
