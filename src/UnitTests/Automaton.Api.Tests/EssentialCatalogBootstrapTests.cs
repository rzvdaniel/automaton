using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class EssentialCatalogBootstrapTests
{
    [Test]
    public async Task BootstrapWaitsForAConnectedRunnerAndFirstAdministrator()
    {
        await using var db = NewDb();
        var roleId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        db.Roles.Add(new ApplicationRole { Id = roleId, Name = UserRole.SystemAdministrator });
        db.UserRoles.Add(new ApplicationUserRole { UserId = adminId, RoleId = roleId });
        var runner = new Runner { Id = Guid.NewGuid(), Name = "first" };
        db.Runners.Add(runner);
        await db.SaveChangesAsync();

        Assert.That(await EssentialCatalogBootstrapService.FirstAdministratorWithRunnerAsync(db, default), Is.EqualTo(Guid.Empty));
        runner.LastConnectedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.That(await EssentialCatalogBootstrapService.FirstAdministratorWithRunnerAsync(db, default), Is.EqualTo(adminId));
    }

    [Test]
    public void BootstrapInstallsOnlyTheDefaultAppsAtTheirCatalogCommit()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var entries = new[] { "server-console", "victorialogs", "website-monitor", "heartbeat" }.Select(id => Entry(id, commit)).ToList();
        entries.Add(Entry("server-console", "ffffffffffffffffffffffffffffffffffffffff", source: "custom"));
        var response = new CatalogResponse(entries, DateTime.UtcNow, false, false, null);

        Assert.That(EssentialCatalogBootstrapService.AppIds, Is.EquivalentTo(new[] { "server-console", "victorialogs" }));
        var (available, skipped) = EssentialCatalogBootstrapService.SelectEssentials(response)!.Value;
        Assert.That(available.Select(app => (app.Id, app.Commit)), Is.EquivalentTo(new[] { ("server-console", commit), ("victorialogs", commit) }));
        Assert.That(skipped, Is.Empty);
        Assert.That(ServerConsoleProvisioningService.DefaultApps["victorialogs"], Is.EqualTo(new[] { "logs:receive" }), "log forwarding needs only this");
        Assert.That(ServerConsoleProvisioningService.DefaultApps.ContainsKey("gate-keeper"), Is.False, "not until it ships");
        Assert.That(EssentialCatalogBootstrapService.SelectEssentials(response with { Unavailable = true }), Is.Null);
        Assert.That(EssentialCatalogBootstrapService.SelectEssentials(response with { Stale = true }), Is.Null);
    }

    [Test]
    public void AMissingOrTooNewCatalogEntrySkipsOnlyThatApp()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var missing = new CatalogResponse([Entry("server-console", commit)], DateTime.UtcNow, false, false, null);
        var (available, skipped) = EssentialCatalogBootstrapService.SelectEssentials(missing)!.Value;
        Assert.That(available.Select(app => app.Id), Is.EqualTo(new[] { "server-console" }));
        Assert.That(skipped, Is.EqualTo(new[] { "victorialogs" }));

        var tooNew = new CatalogResponse([Entry("server-console", commit) with { RequiresNewerServer = true }, Entry("victorialogs", commit)], DateTime.UtcNow, false, false, null);
        (available, skipped) = EssentialCatalogBootstrapService.SelectEssentials(tooNew)!.Value;
        Assert.That(available.Select(app => app.Id), Is.EqualTo(new[] { "victorialogs" }));
        Assert.That(skipped, Is.EqualTo(new[] { "server-console" }));
    }

    [Test]
    public void TheRetryWindowEndsAfterADay()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.That(EssentialCatalogBootstrapService.WindowElapsed(start, start.AddHours(23)), Is.False);
        Assert.That(EssentialCatalogBootstrapService.WindowElapsed(start, start.AddHours(24)), Is.True);
    }

    [Test]
    public async Task AnAppRemovedDuringARetryIsNotInstalledOrApprovedAgain()
    {
        var steps = new List<string>();
        Assert.That(await Installer(find: null, installedByInstaller: true, steps), Is.Null);
        Assert.That(steps, Is.Empty);
    }

    [Test]
    public async Task AnAppTheAdministratorInstalledIsNeverApproved()
    {
        var steps = new List<string>();
        Assert.That(await Installer(find: Guid.NewGuid(), installedByInstaller: false, steps), Is.Null, "VictoriaLogs installed by the administrator keeps logs:receive pending");
        Assert.That(steps, Is.Empty);
    }

    [Test]
    public async Task ANewInstallIsMarkedBeforeItIsApprovedAndARetryOnlyFinishesIt()
    {
        var steps = new List<string>();
        Assert.That(await Installer(find: null, installedByInstaller: false, steps), Is.Not.Null);
        Assert.That(steps, Is.EqualTo(new[] { "install", "mark", "approve", "deploy" }));

        steps.Clear();
        await Installer(find: Guid.NewGuid(), installedByInstaller: true, steps);
        Assert.That(steps, Is.EqualTo(new[] { "approve", "deploy" }));
    }

    private static Task<Guid?> Installer(Guid? find, bool installedByInstaller, List<string> steps) =>
        ServerConsoleProvisioningService.RunInstallerAsync(
            find: () => Task.FromResult(find), install: () => { steps.Add("install"); return Task.FromResult(Guid.NewGuid()); },
            markInstalled: () => { steps.Add("mark"); return Task.CompletedTask; }, installedByInstaller,
            approve: _ => { steps.Add("approve"); return Task.CompletedTask; }, needsDeploy: _ => Task.FromResult(true),
            deploy: _ => { steps.Add("deploy"); return Task.CompletedTask; },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default, "VictoriaLogs");

    [Test]
    public async Task ServerConsoleIsInstalledApprovedAndDeployed()
    {
        var steps = new List<string>();
        var appId = Guid.NewGuid();
        var result = await ServerConsoleProvisioningService.RunAsync(
            find: () => Task.FromResult<Guid?>(null), install: () => { steps.Add("install"); return Task.FromResult(appId); },
            approve: id => { steps.Add("approve"); return Task.CompletedTask; }, needsDeploy: _ => Task.FromResult(true),
            deploy: id => { steps.Add("deploy"); return Task.CompletedTask; },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default, "Server Console");

        Assert.That(result, Is.EqualTo(appId));
        Assert.That(steps, Is.EqualTo(new[] { "install", "approve", "deploy" }));
        Assert.That(ServerConsoleProvisioningService.ApprovedPermissions, Is.EqualTo(new[] { "server:read", "notify:email", "server:cleanup" }));
    }

    [Test]
    public async Task AnInstalledEssentialThatWasNeverDeployedIsDeployed()
    {
        var deployed = new List<Guid>();
        var appId = Guid.NewGuid();
        var result = await ServerConsoleProvisioningService.RunAsync(
            find: () => Task.FromResult<Guid?>(appId), install: () => throw new InvalidOperationException(),
            approve: _ => Task.CompletedTask, needsDeploy: _ => Task.FromResult(true),
            deploy: id => { deployed.Add(id); return Task.CompletedTask; },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default, "Website Monitor");

        Assert.That(result, Is.EqualTo(appId));
        Assert.That(deployed, Is.EqualTo(new[] { appId }));
    }

    private static CatalogEntryDto Entry(string id, string commit, string source = "official") =>
        new(id, id, "", "", "rzvdaniel/automaton-apps", $"maintenance/{id}", commit, "0.0.1", [], false,
            null, source, source, source != "official");

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
