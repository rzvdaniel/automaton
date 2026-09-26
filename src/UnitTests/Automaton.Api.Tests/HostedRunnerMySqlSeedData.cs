using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;

namespace Automaton.Api.Tests;

internal static class HostedRunnerMySqlSeedData
{
    internal static void SeedInfrastructureRunnerHost(ApplicationDbContext db)
    {
        var infrastructureRunnerId = Guid.NewGuid();
        db.Runners.Add(new Runner
        {
            Id = infrastructureRunnerId, Name = "test-infrastructure-runner", Status = "online",
            Purpose = RunnerPurposes.Infrastructure
        });
        db.RunnerHosts.Add(new RunnerHost
        {
            Id = Guid.NewGuid(), Name = "test-runner-host", Provider = RunnerHostProviders.Local,
            Status = RunnerHostStatuses.Active, InfrastructureRunnerId = infrastructureRunnerId,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow, ApprovedAtUtc = DateTime.UtcNow,
            TotalCpuCores = 8, TotalMemoryMb = 16_384, TotalDiskMb = 65_536
        });
        db.SaveChanges();
    }
}
