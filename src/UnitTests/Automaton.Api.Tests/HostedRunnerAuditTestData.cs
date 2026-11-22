using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class HostedRunnerAuditTestData
{
    public static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    public static void SeedUser(ApplicationDbContext db, Guid userId, Guid planId)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = $"user-{userId:N}@automaton.test",
            UserName = $"user-{userId:N}@automaton.test",
            PublicUsername = $"user-{userId:N}"[..32],
            SecurityStamp = Guid.NewGuid().ToString("N")
        });
        db.SaveChanges();
    }

    public static Guid SeedHostedRunnerPlan(ApplicationDbContext db, string? uniqueCodeSuffix = null)
        => SeedPlan(db, enabled: true, uniqueCodeSuffix);

    public static Guid SeedDisabledHostedRunnerPlan(ApplicationDbContext db) => SeedPlan(db, enabled: false);

    public static Guid ConfigureSpecificationResources(ApplicationDbContext db, Guid planId)
    {
        var specification = db.HostedRunnerSpecifications.Single(x => x.Id == planId);
        specification.Resources = new HostedRunnerResources
        {
            CpuLimit = 0.75m,
            MemoryLimitMb = 1024,
            MemoryReservationMb = 512,
            DiskLimitMb = 3072,
            PidsLimit = 192,
            SharedMemoryMb = 96,
            DockerLogMaxSizeMb = 20,
            DockerLogMaxFiles = 4,
            MaximumApps = 7
        };
        db.SaveChanges();
        return specification.Id;
    }

    public static void SeedCoLocatedRunnersWithDifferentDiskLimits(ApplicationDbContext db, Guid userId, Guid planId)
    {
        var first = SeedManagedHostedRunner(db, userId, planId);
        var second = SeedManagedHostedRunner(db, userId, planId);
        second.StorageProjectId = first.StorageProjectId;
        first.SpecificationSnapshot.Resources.DiskLimitMb = 2048;
        second.SpecificationSnapshot.Resources.DiskLimitMb = 2048;
        db.SaveChanges();
    }

    public static void SeedAccountStorageAllocation(
        ApplicationDbContext db,
        Guid accountId,
        int storageProjectId,
        Guid storageRunnerHostId)
    {
        db.AccountStorageAllocations.Add(new AccountStorageAllocation
        {
            AccountId = accountId,
            StorageProjectId = storageProjectId,
            StorageRunnerHostId = storageRunnerHostId
        });
        db.SaveChanges();
    }

    public static string? GetAccountStorageFilesystemUuid(ApplicationDbContext db, Guid accountId)
        => db.AccountStorageAllocations.Single(x => x.AccountId == accountId).StorageFilesystemUuid;

    public static RunnerHost SeedRunnerHost(ApplicationDbContext db)
    {
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-a",
            Provider = RunnerHostProviders.Local,
            Status = RunnerHostStatuses.Active,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            InfrastructureRunnerId = Guid.NewGuid(),
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            TotalCpuCores = 8,
            TotalMemoryMb = 16384,
            TotalDiskMb = 65536,
            CreatedAt = DateTime.UtcNow,
            StorageState = RunnerHostStorageStates.Healthy,
            StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded,
            StorageAvailableBytes = 60L * 1024 * 1024 * 1024,
            StorageReserveRequiredBytes = 5L * 1024 * 1024 * 1024,
            StorageMeasuredAtUtc = DateTime.UtcNow,
            DockerFilesystemTotalBytes = 65536L * 1024 * 1024
        };
        db.RunnerHosts.Add(host);
        db.SaveChanges();
        return host;
    }

    public static HostedRunner SeedManagedHostedRunner(ApplicationDbContext db, Guid userId, Guid? planId = null)
    {
        var host = db.RunnerHosts.FirstOrDefault() ?? SeedRunnerHost(db);
        var effectivePlanId = planId ?? SeedHostedRunnerPlan(db);
        var specificationId = effectivePlanId;
        EnsureUserExists(db, userId, effectivePlanId);
        var runner = new Runner
        {
            Id = Guid.NewGuid(),
            Name = "runner-a",
            Status = "online",
            LastConnectedAtUtc = DateTime.UtcNow
        };
        var hostedRunner = new HostedRunner
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RunnerHostId = host.Id,
            RunnerHost = host,
            RunnerId = runner.Id,
            Runner = runner,
            Name = "Hosted Runner",
            Status = HostedRunnerStatuses.Running,
            ContainerName = "automaton-hosted-runner-test",
            HostedRunnerSpecificationId = specificationId,
            SpecificationRevision = 1,
            StorageProjectId = 1_000_000 + db.HostedRunners.Count(),
            CreatedAt = DateTime.UtcNow
        };
        db.Runners.Add(runner);
        db.HostedRunners.Add(hostedRunner);
        db.SaveChanges();
        return hostedRunner;
    }

    private static Guid SeedPlan(ApplicationDbContext db, bool enabled, string? uniqueCodeSuffix = null)
    {
        var codeSuffix = string.IsNullOrWhiteSpace(uniqueCodeSuffix) ? string.Empty : $"-{uniqueCodeSuffix}";
        var planName = enabled ? "Hosted" : "No Hosted";
        var specification = new HostedRunnerSpecification
        {
            Id = Guid.NewGuid(),
            Code = enabled ? $"test-hosted{codeSuffix}" : $"test-disabled{codeSuffix}",
            Name = $"{planName}{codeSuffix} legacy specification",
            IsActive = true
        };
        db.HostedRunnerSpecifications.Add(specification);
        db.SaveChanges();
        return specification.Id;
    }

    private static void EnsureUserExists(ApplicationDbContext db, Guid userId, Guid planId)
    {
        if (db.Users.Any(x => x.Id == userId))
        {
            return;
        }

        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = $"user-{userId:N}@automaton.test",
            UserName = $"user-{userId:N}@automaton.test",
            PublicUsername = $"user-{userId:N}"[..32],
            SecurityStamp = Guid.NewGuid().ToString("N")
        });
    }
}

internal static class HostedRunnerAuditExecutionData
{
    public static AppModuleExecution CreateInfrastructureExecution(Guid operationId) => new()
    {
        Id = Guid.NewGuid(),
        HostedRunnerOperationId = operationId,
        AppId = InfrastructureAppTrust.PlatformAppId,
        DeploymentTargetId = Guid.NewGuid(),
        ModuleType = AppModuleTypes.Action,
        ModuleKey = "restart-hosted-runner",
        TriggerType = AppModuleTriggerTypes.Internal,
        Status = AppModuleExecutionStatuses.Running,
        CreatedAtUtc = DateTime.UtcNow
    };
}
