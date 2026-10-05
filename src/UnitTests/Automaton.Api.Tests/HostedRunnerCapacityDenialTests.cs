using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Controllers.Admin;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class HostedRunnerCapacityDenialTests
{
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public async Task CapacityDenialReturnsConflictWithoutAddingReservationsOrDispatch(bool cpu, bool existingPending)
    {
        await using var fixture = await HostedRunnerCapacityDenialFixture.CreateAsync(cpu, existingPending);
        var response = await fixture.Controller.ReprovisionHostedRunner(fixture.RunnerId, CancellationToken.None);

        var conflict = response.Result as ConflictObjectResult;
        Assert.That(conflict, Is.Not.Null);
        Assert.That(conflict!.StatusCode, Is.EqualTo(409));
        Assert.That(JsonSerializer.Serialize(conflict.Value), Does.Contain("No active runner host has enough available capacity."));
        await fixture.AssertDeniedStateAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SameRequestCanBeAdmittedAfterCapacityIsRestored(bool cpu)
    {
        await using var fixture = await HostedRunnerCapacityDenialFixture.CreateAsync(cpu, existingPending: false);
        await fixture.Controller.ReprovisionHostedRunner(fixture.RunnerId, CancellationToken.None);
        await fixture.AssertDeniedStateAsync();
        await fixture.RestoreCapacityAsync();

        var response = await fixture.Controller.ReprovisionHostedRunner(fixture.RunnerId, CancellationToken.None);

        Assert.That(response.Result, Is.InstanceOf<OkObjectResult>());
        await fixture.AssertAdmittedStateAsync();
    }
}

internal sealed class HostedRunnerCapacityDenialFixture(ApplicationDbContext db, HostedRunner runner) : IAsyncDisposable
{
    private readonly string originalState = ReservationState(runner);
    public Guid RunnerId { get; } = runner.Id;
    public HostedRunnerRecordingEventPublisher Publisher { get; } = new();
    public AdminHostedRunnersController Controller { get; private set; } = null!;

    public static async Task<HostedRunnerCapacityDenialFixture> CreateAsync(bool cpu, bool existingPending)
    {
        var db = HostedRunnerAuditTestData.CreateDbContext();
        var accountId = Guid.NewGuid();
        var planId = HostedRunnerAuditTestData.SeedHostedRunnerPlan(db);
        HostedRunnerAuditTestData.ConfigureSpecificationResources(db, planId);
        var host = HostedRunnerAuditTestData.SeedRunnerHost(db);
        host.TotalCpuCores = cpu ? 1 : 8;
        host.ReservedCpuCores = cpu ? 0.25m : 0;
        host.TotalMemoryMb = cpu ? 16384 : 1024;
        host.ReservedMemoryMb = cpu ? 0 : 256;
        var runner = SeedRetainedRunner(db, accountId, planId, existingPending);
        var competing = HostedRunnerAuditTestData.SeedManagedHostedRunner(db, Guid.NewGuid(), planId);
        competing.ContainerId = "competing-container";
        competing.SpecificationSnapshot.Resources.CpuLimit = 0.25m;
        competing.SpecificationSnapshot.Resources.MemoryLimitMb = 256;
        await db.SaveChangesAsync();
        var fixture = new HostedRunnerCapacityDenialFixture(db, runner);
        fixture.Controller = new AdminHostedRunnersController(db, null!,
            HostedRunnerManagementTestServiceFactory.Create(db, accountId, fixture.Publisher), null!);
        return fixture;
    }

    private static HostedRunner SeedRetainedRunner(ApplicationDbContext db, Guid accountId, Guid planId, bool pending)
    {
        var runner = HostedRunnerAuditTestData.SeedManagedHostedRunner(db, accountId, planId);
        runner.ContainerId = "existing-container";
        runner.Runner!.ConnectionId = "existing-connection";
        runner.RuntimePort = 23456;
        runner.RuntimeEndpoint = "http://existing-runner:23456";
        runner.SpecificationSnapshot.Resources.CpuLimit = 0.25m;
        runner.SpecificationSnapshot.Resources.MemoryLimitMb = 384;
        runner.SpecificationSnapshot.Resources.DiskLimitMb = 2048;
        runner.StorageAllowanceBytes = 2048L * 1024 * 1024;
        if (pending)
            runner.PendingSpecificationSnapshot = new HostedRunnerSpecificationSnapshot
            {
                Resources = new HostedRunnerResources { CpuLimit = 0.5m, MemoryLimitMb = 512, DiskLimitMb = 2048 }
            };
        return runner;
    }

    public async Task AssertDeniedStateAsync()
    {
        db.ChangeTracker.Clear();
        var persisted = await db.HostedRunners.Include(x => x.Runner).SingleAsync(x => x.Id == RunnerId);
        Assert.That(ReservationState(persisted), Is.EqualTo(originalState));
        Assert.That(persisted.Runner!.ConnectionId, Is.EqualTo("existing-connection"));
        Assert.That(persisted.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
        Assert.That(await db.RunnerEnrollments.CountAsync(), Is.Zero);
        Assert.That(await db.ExecutionSecretGrants.CountAsync(), Is.Zero);
        Assert.That(Publisher.Messages, Is.Empty);
        Assert.That(await db.HostedRunnerDeployments.CountAsync(), Is.EqualTo(1));
    }

    public async Task RestoreCapacityAsync()
    {
        var host = await db.RunnerHosts.SingleAsync();
        host.TotalCpuCores = 8;
        host.TotalMemoryMb = 16384;
        await db.SaveChangesAsync();
    }

    public async Task AssertAdmittedStateAsync()
    {
        db.ChangeTracker.Clear();
        var persisted = await db.HostedRunners.SingleAsync(x => x.Id == RunnerId);
        Assert.That(persisted.PendingSpecificationSnapshot!.Resources.CpuLimit, Is.EqualTo(0.75m));
        Assert.That(persisted.PendingSpecificationSnapshot.Resources.MemoryLimitMb, Is.EqualTo(1024));
        Assert.That(persisted.SpecificationSnapshot.Resources.CpuLimit, Is.EqualTo(0.25m));
        Assert.That(persisted.StorageAllowanceBytes, Is.EqualTo(3072L * 1024 * 1024));
        Assert.That(Publisher.Messages, Has.Count.EqualTo(1));
    }

    private static string ReservationState(HostedRunner value) => JsonSerializer.Serialize(new
    {
        value.RunnerId, value.RunnerHostId, value.ContainerId, value.RuntimeEndpoint, value.RuntimePort,
        value.StorageAllowanceBytes, value.SpecificationSnapshot, value.PendingSpecificationSnapshot
    });

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
