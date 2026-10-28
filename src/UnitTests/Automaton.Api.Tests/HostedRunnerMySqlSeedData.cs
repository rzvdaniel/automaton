using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;

namespace Automaton.Api.Tests;

internal static class HostedRunnerMySqlSeedData
{
    internal static RunnerHost SeedInfrastructureRunnerHost(ApplicationDbContext db)
    {
        var infrastructureRunnerId = Guid.NewGuid();
        db.Runners.Add(new Runner
        {
            Id = infrastructureRunnerId, Name = "test-infrastructure-runner", Status = "online",
            Purpose = "Infrastructure"
        });
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(), Name = "test-runner-host", Provider = RunnerHostProviders.Local,
            Status = RunnerHostStatuses.Active, InfrastructureRunnerId = infrastructureRunnerId,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow, ApprovedAtUtc = DateTime.UtcNow,
            TotalCpuCores = 8, TotalMemoryMb = 16_384, TotalDiskMb = 65_536,
            StorageState = RunnerHostStorageStates.Healthy,
            StorageAvailableBytes = 60L * 1024 * 1024 * 1024,
            StorageReserveRequiredBytes = 0, StorageMeasuredAtUtc = DateTime.UtcNow,
            DockerFilesystemTotalBytes = 65_536L * 1024 * 1024
        };
        db.RunnerHosts.Add(host);
        db.SaveChanges();
        return host;
    }
    internal static (RunnerHost Host, Guid[] AccountIds) SeedComputeFixture(ApplicationDbContext db, bool cpu, bool resize)
    {
        var host = HostedRunnerMySqlSeedData.SeedInfrastructureRunnerHost(db);
        host.TotalCpuCores = cpu ? 1 : 8;
        host.TotalMemoryMb = cpu ? 8192 : 1024;
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db, Guid.NewGuid().ToString("N")[..8]);
        var specificationId = HostedRunnerAuditTestData.ConfigureSpecificationResources(db, planId);
        var specification = db.HostedRunnerSpecifications.Single(x => x.Id == specificationId);
        specification.Resources.CpuLimit = cpu ? 0.75m : 0.25m;
        specification.Resources.MemoryLimitMb = cpu ? 384 : 768;
        specification.Resources.MemoryReservationMb = 256;
        var accountIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var accountId in accountIds)
        {
            HostedRunnerAuditTestData.SeedUser(db, accountId, planId);
            HostedRunnerAuditTestData.SeedAccountStorageAllocation(db, accountId,
                Random.Shared.Next(2_000_000, int.MaxValue), host.Id);
        }
        if (resize)
        {
            var allocation = db.AccountStorageAllocations.Single(x => x.AccountId == accountIds[0]);
            db.HostedRunners.Add(new HostedRunner
            {
                Id = Guid.NewGuid(), UserId = accountIds[0], RunnerHostId = host.Id,
                HostedRunnerSpecificationId = specificationId, SpecificationRevision = 1,
                Status = HostedRunnerStatuses.Running, ContainerId = "retained-test-container",
                StorageProjectId = allocation.StorageProjectId,
                SpecificationSnapshot = new HostedRunnerSpecificationSnapshot
                {
                    SourceSpecificationId = specificationId, Resources = new HostedRunnerResources
                    {
                        CpuLimit = 0.25m, MemoryLimitMb = 384, MemoryReservationMb = 256,
                        DiskLimitMb = specification.Resources.DiskLimitMb
                    }
                }
            });
        }
        db.SaveChanges();
        return (host, accountIds);
    }

}
