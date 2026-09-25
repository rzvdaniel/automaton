using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Constants;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class HostedRunnerPlanChangeTestScenario
{
    public static HostedRunnerPlanChangeScenario Seed(ApplicationDbContext db, Guid userId)
    {
        var specificationA = CreateSpecification("test-a", "Test A", 0.25m, 768, 2048);
        var specificationB = CreateSpecification("test-b", "Test B", 2m, 4096, 8192);
        var planA = CreatePlan("Plan A", specificationA.Id);
        var planB = CreatePlan("Plan B", specificationB.Id);
        db.HostedRunnerSpecifications.AddRange(specificationA, specificationB);
        db.SubscriptionPlans.AddRange(planA, planB);
        HostedRunnerAuditTestData.SeedUser(db, userId, planA.Id);

        var currentHost = HostedRunnerAuditTestData.SeedRunnerHost(db);
        ConfigureCurrentHost(currentHost);
        db.RunnerHosts.Add(CreateAlternateHost());
        db.SaveChanges();

        var hostedRunner = HostedRunnerAuditTestData.SeedManagedHostedRunner(db, userId, planA.Id);
        hostedRunner.RunnerHostId = currentHost.Id;
        hostedRunner.RunnerHost = currentHost;
        hostedRunner.RuntimePort = 20000;
        hostedRunner.RuntimeEndpoint = "http://203.0.113.1:20000";
        hostedRunner.SpecificationSnapshot = HostedRunnerSpecificationSnapshotFactory.Create(
            specificationA,
            DateTime.UtcNow.AddMinutes(-5));
        hostedRunner.PendingSpecificationSnapshot = null;
        db.Users.Single(x => x.Id == userId).SubscriptionPlanId = planB.Id;
        db.SaveChanges();

        return new HostedRunnerPlanChangeScenario(specificationA.Id, currentHost.Id);
    }

    public static async Task AssertRunningRunnerRemainsUnchangedAsync(
        ApplicationDbContext db,
        HostedRunnerPreparationResult prepared,
        HostedRunnerPlanChangeScenario scenario)
    {
        Assert.That(prepared.ShouldPublishProvisionRequest, Is.False);
        Assert.That(prepared.HostedRunner.RunnerHostId, Is.EqualTo(scenario.RunnerHostId));
        Assert.That(prepared.HostedRunner.RuntimePort, Is.EqualTo(20000));
        Assert.That(prepared.HostedRunner.RuntimeEndpoint, Is.EqualTo("http://203.0.113.1:20000"));
        Assert.That(prepared.HostedRunner.SpecificationSnapshot.SourceSpecificationId, Is.EqualTo(scenario.SpecificationId));
        Assert.That(prepared.HostedRunner.PendingSpecificationSnapshot, Is.Null);
        Assert.That(await db.AuditEvents.CountAsync(x => x.EventType == AuditEventTypes.HostedRunnerProvisioningRequested), Is.Zero);
    }

    private static HostedRunnerSpecification CreateSpecification(
        string code,
        string name,
        decimal cpuLimit,
        int memoryLimitMb,
        int diskLimitMb) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        IsActive = true,
        Resources = new HostedRunnerResources
        {
            CpuLimit = cpuLimit,
            MemoryLimitMb = memoryLimitMb,
            DiskLimitMb = diskLimitMb
        }
    };

    private static SubscriptionPlan CreatePlan(string name, Guid specificationId) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsActive = true,
        CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(new PlanCapabilities
        {
            HostedRunner = new HostedRunnerCapability { Enabled = true, SpecificationId = specificationId }
        })
    };

    private static void ConfigureCurrentHost(RunnerHost host)
    {
        host.ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner;
        host.RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort;
        host.PublicIp = "203.0.113.1";
        host.InfrastructureRunnerId = Guid.NewGuid();
        host.TotalCpuCores = 1;
        host.TotalMemoryMb = 1024;
        host.TotalDiskMb = 4096;
    }

    private static RunnerHost CreateAlternateHost() => new()
    {
        Id = Guid.NewGuid(),
        Name = "host-b",
        Provider = RunnerHostProviders.Hetzner,
        PublicIp = "203.0.113.2",
        InfrastructureRunnerId = Guid.NewGuid(),
        Status = RunnerHostStatuses.Active,
        ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
        RuntimeConnectivityMode = RunnerHostRuntimeConnectivityModes.PublicHostPort,
        ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
        ValidatedAtUtc = DateTime.UtcNow,
        ApprovedAtUtc = DateTime.UtcNow,
        TotalCpuCores = 8,
        TotalMemoryMb = 16384,
        TotalDiskMb = 65536,
        CreatedAt = DateTime.UtcNow.AddMinutes(1)
    };
}

internal sealed record HostedRunnerPlanChangeScenario(Guid SpecificationId, Guid RunnerHostId);
