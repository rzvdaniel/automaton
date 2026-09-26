using System.Security.Claims;
using System.Data.Common;
using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Automaton.Core.Constants;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;


namespace Automaton.Api.Tests;

[TestFixture]
public class HostedRunnerProvisioningPolicyAuditTests
{

    [Test]
    public async Task EnsureHostedRunnerForUserPlanAsync_WritesRequestedAuditAndQueuesProvisioning()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedUser(db, userId, planId);
        HostedRunnerAuditTestData.SeedRunnerHost(db);

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            userId,
            new HostedRunnerOptions
            {
                Enabled = true
            });

        await HostedRunnerProvisioningAuditAssertions.AssertRequestedAuditAndQueuedRunnerAsync(db, service, userId);
    }


    [Test]
    public async Task EnsureHostedRunnerForUserPlanAsync_DoesNotRunDockerSynchronously()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedUser(db, userId, planId);
        HostedRunnerAuditTestData.SeedRunnerHost(db);

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            userId,
            new HostedRunnerOptions
            {
                Enabled = true
            });

        var runner = await service.EnsureHostedRunnerAsync(userId);

        Assert.That(runner, Is.Not.Null);

        Assert.That(runner!.Status, Is.EqualTo(HostedRunnerStatuses.Provisioning));
        Assert.That(await db.AuditEvents.AnyAsync(x => x.EventType == AuditEventTypes.HostedRunnerProvisioningFailed), Is.False);
    }


    [Test]
    public async Task PrepareProvisioningAsync_PersistsResolvedPolicyAndPreservesSpecificationSettings()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedUser(db, userId, planId);
        HostedRunnerAuditTestData.SeedRunnerHost(db);

        var specificationId = HostedRunnerAuditTestData.ConfigureSpecificationResources(db, planId);

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            userId,
            new HostedRunnerOptions { Enabled = false });

        var prepared = await service.PrepareProvisioningAsync(userId, Guid.Empty);
        HostedRunnerProvisioningStorageAssertions.AssertResolvedSpecification(prepared, specificationId);
        await HostedRunnerProvisioningStorageAssertions.AssertAccountProjectRecordedAsync(db, prepared, userId);
    }


    [Test]
    public async Task PrepareProvisioningAsync_ReusesAccountProjectAcrossReprovisionAndRunnerRecreation()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.SeedUser(db, userId, planId);
        HostedRunnerAuditTestData.SeedRunnerHost(db);
        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            userId,
            new HostedRunnerOptions { Enabled = false });

        await HostedRunnerProvisioningStorageAssertions.AssertReprovisionReusesAccountProjectAsync(db, service, userId);
    }


    [Test]
    public async Task PrepareProvisioningAsync_DoesNotOverallocateHostCapacity()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        var host = HostedRunnerAuditTestData.SeedRunnerHost(db);
        // The Free specification requests 0.50 CPU, so this host can fit one
        // Runner but must reject a second.
        host.TotalCpuCores = 0.75m;
        host.TotalMemoryMb = 1024;
        host.TotalDiskMb = 4096;
        db.SaveChanges();

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            Guid.NewGuid(),
            new HostedRunnerOptions { Enabled = false });

        var first = await service.PrepareProvisioningAsync(Guid.NewGuid(), planId);
        var second = await service.PrepareProvisioningAsync(Guid.NewGuid(), planId);

        Assert.That(first.HostedRunner.RunnerHostId, Is.EqualTo(host.Id));
        Assert.That(first.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Pending));
        Assert.That(second.HostedRunner.RunnerHostId, Is.Null);
        Assert.That(second.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
        Assert.That(await db.HostedRunners.CountAsync(x => x.RunnerHostId == host.Id), Is.EqualTo(1));
    }


    [Test]
    public async Task PrepareProvisioningAsync_DoesNotSelectLocalHostWhenSchedulingIsDisabled()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        var localHost = HostedRunnerAuditTestData.SeedRunnerHost(db);
        localHost.ProvisioningTransport = RunnerHostProvisioningTransports.LocalDocker;
        localHost.InfrastructureRunnerId = null;
        localHost.HostedRunnerSchedulingEnabled = false;

        var remoteHost = new RunnerHost
        {
            Id = Guid.NewGuid(),
            Name = "host-remote",
            Provider = RunnerHostProviders.Hetzner,
            PublicIp = "203.0.113.10",
            InfrastructureRunnerId = Guid.NewGuid(),
            Status = RunnerHostStatuses.Active,
            ProvisioningTransport = RunnerHostProvisioningTransports.InfrastructureRunner,
            ProvisioningStatus = RunnerHostProvisioningStatuses.Ready,
            ValidatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            HostedRunnerSchedulingEnabled = true,
            TotalCpuCores = 8,
            TotalMemoryMb = 16384,
            TotalDiskMb = 65536,
            CreatedAt = DateTime.UtcNow.AddMinutes(1)
        };
        db.RunnerHosts.Add(remoteHost);
        db.SaveChanges();

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            Guid.NewGuid(),
            new HostedRunnerOptions { Enabled = false });

        var prepared = await service.PrepareProvisioningAsync(Guid.NewGuid(), planId);

        Assert.That(prepared.HostedRunner.RunnerHostId, Is.EqualTo(remoteHost.Id));
        Assert.That(prepared.HostedRunner.RunnerHostId, Is.Not.EqualTo(localHost.Id));
    }


    [Test]
    public async Task PrepareProvisioningAsync_DoesNotSelectLocalDockerHostWithoutInfrastructureRunner()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        var localHost = HostedRunnerAuditTestData.SeedRunnerHost(db);
        localHost.ProvisioningTransport = RunnerHostProvisioningTransports.LocalDocker;
        localHost.InfrastructureRunnerId = null;
        localHost.HostedRunnerSchedulingEnabled = true;
        db.SaveChanges();

        var service = HostedRunnerProvisioningTestServiceFactory.Create(
            db,
            Guid.NewGuid(),
            new HostedRunnerOptions { Enabled = false });

        var prepared = await service.PrepareProvisioningAsync(Guid.NewGuid(), planId);

        Assert.That(prepared.HostedRunner.RunnerHostId, Is.Null);
        Assert.That(prepared.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
    }
}
