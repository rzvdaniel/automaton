using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class LogSinkServiceTests
{
    private const string Address = "http://172.19.0.1:32784";

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Fixture(ApplicationDbContext Db, Clock Clock, LogSinkService Service, Guid AppId, Guid TargetId, Guid RunnerId)
    {
        public LogSinkRuntimeReport Report(string address = Address) => new(AppId, TargetId, address);
    }

    private static async Task<Fixture> CreateAsync(bool requests = true, string status = AppPermissionStatuses.Approved)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        var appId = Guid.NewGuid();
        var runnerId = Guid.NewGuid();
        AppAuthorizationTestData.SeedApp(db, appId, Guid.NewGuid(), "logs");
        var app = await db.WebApps.SingleAsync();
        app.ApplicationContractJson = ApplicationContractSerialization.Serialize(new ApplicationContract
        {
            Permissions = requests ? [AppPermissionApproval.LogsReceive] : []
        });
        var target = await db.AppDeploymentTargets.SingleAsync(item => item.WebAppId == appId);
        db.Runners.Add(new Runner { Id = runnerId, Name = "runner", Purpose = RunnerPurposes.Managed, ConnectionId = "connection", Status = "online" });
        target.ExecutionRunnerId = runnerId;
        db.AppPermissionApprovals.Add(new AppPermissionApproval { Id = Guid.NewGuid(), AppId = appId, Permission = AppPermissionApproval.LogsReceive, Status = status });
        await db.SaveChangesAsync();
        var clock = new Clock(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        return new Fixture(db, clock, new LogSinkService(db, clock), appId, target.Id, runnerId);
    }

    [Test]
    public async Task Approved_app_on_the_reporting_runner_receives_logs_and_is_resolved_for_API_and_Worker()
    {
        var f = await CreateAsync();

        var assignment = await f.Service.SyncAsync(f.RunnerId, [f.Report()], default);

        Assert.That(assignment, Is.EqualTo(new LogSinkAssignment(f.AppId, f.TargetId, Address)));
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.EqualTo(Address));
    }

    [Test]
    public async Task A_new_port_after_a_restart_replaces_the_address()
    {
        var f = await CreateAsync();
        await f.Service.SyncAsync(f.RunnerId, [f.Report()], default);

        await f.Service.SyncAsync(f.RunnerId, [f.Report("http://172.19.0.1:40000")], default);

        Assert.That(await f.Service.ResolveAddressAsync(default), Is.EqualTo("http://172.19.0.1:40000"));
    }

    [TestCase(AppPermissionStatuses.Pending)]
    [TestCase(AppPermissionStatuses.Rejected)]
    [TestCase(AppPermissionStatuses.Revoked)]
    public async Task Unapproved_app_receives_nothing(string status)
    {
        var f = await CreateAsync(status: status);

        Assert.That(await f.Service.SyncAsync(f.RunnerId, [f.Report()], default), Is.Null);
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.Null);
    }

    [Test]
    public async Task App_whose_contract_no_longer_asks_receives_nothing()
    {
        var f = await CreateAsync(requests: false);

        Assert.That(await f.Service.SyncAsync(f.RunnerId, [f.Report()], default), Is.Null);
    }

    [Test]
    public async Task Another_runner_cannot_name_an_app_it_does_not_host()
    {
        var f = await CreateAsync();

        Assert.That(await f.Service.SyncAsync(Guid.NewGuid(), [f.Report()], default), Is.Null);
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.Null);
    }

    [Test]
    public async Task Pausing_revoking_or_a_silent_runner_stops_forwarding()
    {
        var f = await CreateAsync();
        await f.Service.SyncAsync(f.RunnerId, [f.Report()], default);
        var app = await f.Db.WebApps.SingleAsync();

        app.IsPaused = true;
        await f.Db.SaveChangesAsync();
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.Null, "paused");

        app.IsPaused = false;
        await f.Db.SaveChangesAsync();
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.EqualTo(Address));

        var approval = await f.Db.AppPermissionApprovals.SingleAsync();
        approval.Status = AppPermissionStatuses.Revoked;
        await f.Db.SaveChangesAsync();
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.Null, "revoked");

        approval.Status = AppPermissionStatuses.Approved;
        await f.Db.SaveChangesAsync();
        f.Clock.Now += LogSinkService.Freshness + TimeSpan.FromSeconds(1);
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.Null, "runner stopped reporting");
    }

    [Test]
    public async Task A_runner_that_stops_hosting_the_app_clears_its_record_but_not_another_runners()
    {
        var f = await CreateAsync();
        await f.Service.SyncAsync(f.RunnerId, [f.Report()], default);

        await f.Service.SyncAsync(Guid.NewGuid(), [], default);
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.EqualTo(Address), "another runner cannot clear it");

        await f.Service.SyncAsync(f.RunnerId, [], default);
        Assert.That(await f.Db.Settings.AnyAsync(), Is.False);
        Assert.That(await f.Service.ResolveAddressAsync(default), Is.Null);
    }

    [TestCase("http://172.19.0.1:32784", "http://172.19.0.1:32784")]
    [TestCase("http://10.0.0.5:9000/", "http://10.0.0.5:9000")]
    [TestCase("http://192.168.1.2:8080", "http://192.168.1.2:8080")]
    [TestCase("https://172.19.0.1:32784", null)]
    [TestCase("http://8.8.8.8:9000", null)]
    [TestCase("http://127.0.0.1:9000", null)]
    [TestCase("http://logs.example:9000", null)]
    [TestCase("http://172.19.0.1", null)]
    [TestCase("http://172.19.0.1:80", null)]
    [TestCase("http://172.19.0.1:9000/x", null)]
    [TestCase("http://u:p@172.19.0.1:9000", null)]
    [TestCase("http://[fd00::1]:9000", null)]
    [TestCase(null, null)]
    public void Only_private_http_addresses_are_used(string? value, string? expected)
        => Assert.That(LogSinkService.NormalizeAddress(value), Is.EqualTo(expected));

    [Test]
    public async Task A_public_address_reported_by_a_runner_is_ignored()
    {
        var f = await CreateAsync();

        Assert.That(await f.Service.SyncAsync(f.RunnerId, [f.Report("http://8.8.8.8:9000")], default), Is.Null);
    }

    [Test]
    public void The_permission_is_supported_in_automaton_json()
        => Assert.That(ApplicationContractValidator.SupportedPermissions, Does.Contain("logs:receive"));
}
