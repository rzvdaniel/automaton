using Automaton.Api.Authentication;
using Automaton.Api.Controllers;
using Automaton.Api.Entities;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class ServerCleanupTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 5, 1, 0, DateTimeKind.Utc);

    private static (ServerCleanupService Service, FakeRunner Fake, Automaton.Api.Data.ApplicationDbContext Db) SetUp(bool online = true)
    {
        var db = AppAuthorizationTestData.CreateDbContext();
        db.Runners.Add(new Runner
        {
            Id = Guid.NewGuid(), Name = "runner-01", Purpose = RunnerPurposes.Managed, ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            InstallationId = $"sh_{Guid.NewGuid():N}", ConnectionId = online ? "c" : null, LastHeartbeatAtUtc = Now
        });
        db.SaveChanges();
        var fake = new FakeRunner { Response = new HostCleanupResult(1_800_000_000, "Docker images: freed 1.8 GB.") };
        return (new ServerCleanupService(db, fake), fake, db);
    }

    // Settings that were last changed yesterday, as on a server that has been running.
    private static async Task Backdate(ServerCleanupService service, Automaton.Api.Data.ApplicationDbContext db)
    {
        await service.GetAsync(Now, default);
        db.ServerCleanupSettings.Single().UpdatedAtUtc = Now.AddDays(-1);
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task ChangingOnlyTheKeepDaysDoesNotSkipTonightsRun()
    {
        var (service, _, db) = SetUp();
        await Backdate(service, db);
        await service.UpdateAsync(new("05:00", 3, 200), Now.AddMinutes(-1), default);
        await service.ProcessAsync(Now, default);
        Assert.That((await service.GetAsync(Now, default)).Runs, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DefaultsAreFiveAmSevenDaysAndTwoHundredMegabytes()
    {
        var (service, _, _) = SetUp();
        var state = await service.GetAsync(Now, default);
        Assert.That(state.Settings, Is.EqualTo(new ServerCleanupSettingsDto("05:00", 7, 200)));
        Assert.That(state.NextRunAtUtc, Is.EqualTo(new DateTime(2026, 11, 14, 5, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task ChangingTheTimeMovesTheNextRun()
    {
        var (service, _, _) = SetUp();
        var state = await service.UpdateAsync(new("02:30", 3, 100), Now, default);
        Assert.That(state.Settings, Is.EqualTo(new ServerCleanupSettingsDto("02:30", 3, 100)));
        Assert.That(state.NextRunAtUtc, Is.EqualTo(new DateTime(2026, 11, 14, 2, 30, 0, DateTimeKind.Utc)));
    }

    [TestCase("5", 7, 200)]
    [TestCase("25:00", 7, 200)]
    [TestCase("05:00", 0, 200)]
    [TestCase("05:00", 7, 10)]
    public void RejectsBadSettings(string time, int days, int journal)
    {
        var (service, _, _) = SetUp();
        Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(new(time, days, journal), Now, default));
    }

    [Test]
    public async Task RunNowRecordsTheRunWithBytesFreedAndSendsTheSettings()
    {
        var (service, fake, _) = SetUp();
        await service.UpdateAsync(new("02:00", 3, 150), Now.AddHours(-10), default);
        await service.RunNowAsync(Now, default);
        await service.ProcessAsync(Now, default);

        var run = (await service.GetAsync(Now, default)).Runs.Single();
        Assert.That(run.Result, Is.EqualTo("succeeded"));
        Assert.That(run.BytesFreed, Is.EqualTo(1_800_000_000));
        Assert.That(run.Trigger, Is.EqualTo("manual"));
        Assert.That(fake.Kind, Is.EqualTo("host-cleanup"));
        Assert.That(fake.Payload, Is.EqualTo(new HostCleanupRequest(3, 150)));
    }

    [Test]
    public async Task ARunnerFailureIsRecordedAsAFailedRun()
    {
        var (service, fake, _) = SetUp();
        fake.Error = new InvalidOperationException("Docker cleanup failed: daemon down");
        await service.RunNowAsync(Now, default);
        await service.ProcessAsync(Now, default);
        var run = (await service.GetAsync(Now, default)).Runs.Single();
        Assert.That(run.Result, Is.EqualTo("failed"));
        Assert.That(run.Message, Does.Contain("daemon down"));
    }

    [Test]
    public void RunNowWithoutAnOnlineRunnerIsRefused()
        => Assert.ThrowsAsync<InvalidOperationException>(() => SetUp(online: false).Service.RunNowAsync(Now, default));

    [Test]
    public async Task TheNightlyRunHappensOncePerDayAfterItsTime()
    {
        var (service, fake, db) = SetUp();
        await Backdate(service, db);
        await service.ProcessAsync(Now.AddMinutes(-2), default); // 04:59, not yet
        Assert.That((await service.GetAsync(Now, default)).Runs, Is.Empty);

        await service.ProcessAsync(Now, default);
        await service.ProcessAsync(Now.AddMinutes(5), default);
        var runs = (await service.GetAsync(Now, default)).Runs;
        Assert.That(runs, Has.Count.EqualTo(1));
        Assert.That(runs[0].Trigger, Is.EqualTo("scheduled"));
    }

    [Test]
    public async Task ATimeChosenAfterItHasPassedTodayWaitsForTomorrow()
    {
        var (service, _, _) = SetUp();
        await service.UpdateAsync(new("02:00", 7, 200), Now, default);
        await service.ProcessAsync(Now, default);
        Assert.That((await service.GetAsync(Now, default)).Runs, Is.Empty);
    }

    [Test]
    public async Task ANightlyRunWithNoOnlineRunnerShowsAsFailed()
    {
        var (service, _, db) = SetUp(online: false);
        await Backdate(service, db);
        await service.ProcessAsync(Now, default);
        var run = (await service.GetAsync(Now, default)).Runs.Single();
        Assert.That(run.Result, Is.EqualTo("failed"));
        Assert.That(run.Message, Does.Contain("No server Runner"));
    }

    [Test]
    public void OnlyAdministratorsAndApprovedAppsReachTheEndpoints()
    {
        var policy = typeof(ServerCleanupController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.That(policy.Policy, Is.EqualTo(AppPermissionAuthenticationOptions.ServerCleanupPolicy));
        Assert.That(typeof(ServerCleanupController).GetMethods().SelectMany(m => m.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)), Is.Empty);
    }

    [Test]
    public async Task OnlyASystemAdministratorOrACleanupCredentialPassesThePolicy()
    {
        var services = new ServiceCollection().AddLogging()
            .AddAuthorization(options => AppPermissionAuthenticationOptions.AddPermissionPolicy(options,
                AppPermissionAuthenticationOptions.ServerCleanupPolicy, AppPermissionApproval.ServerCleanup)).BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        async Task<bool> Allowed(ClaimsPrincipal user) =>
            (await authorization.AuthorizeAsync(user, AppPermissionAuthenticationOptions.ServerCleanupPolicy)).Succeeded;
        static ClaimsPrincipal App(string permission) => new(new ClaimsIdentity([new Claim("app_permission", permission)], "AppPermission"));
        static ClaimsPrincipal Role(string role) => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Identity.Application"));

        Assert.That(await Allowed(Role(Automaton.Api.Enums.UserRole.SystemAdministrator)), Is.True);
        Assert.That(await Allowed(App(AppPermissionApproval.ServerCleanup)), Is.True);
        Assert.That(await Allowed(App(AppPermissionApproval.ServerRead)), Is.False, "a health-only key cannot change the cleanup");
        Assert.That(await Allowed(Role(Automaton.Api.Enums.UserRole.User)), Is.False);
        Assert.That(await Allowed(new ClaimsPrincipal(new ClaimsIdentity())), Is.False, "anonymous");
    }
}
