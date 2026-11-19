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
        var missing = EssentialCatalogBootstrapService.MissingEssentials(response, new HashSet<(string Repo, string Folder)>());
        Assert.That(missing!.Select(app => (app.Id, app.Commit)), Is.EquivalentTo(new[] { ("server-console", commit), ("victorialogs", commit) }));
        Assert.That(EssentialCatalogBootstrapService.MissingEssentials(response, new HashSet<(string Repo, string Folder)>
            { ("rzvdaniel/automaton-apps", "maintenance/server-console"), ("rzvdaniel/automaton-apps", "maintenance/victorialogs") }), Is.Empty, "an existing install is kept");
        Assert.That(ServerConsoleProvisioningService.DefaultApps["victorialogs"], Is.EqualTo(new[] { "logs:receive" }), "log forwarding needs only this");
        Assert.That(ServerConsoleProvisioningService.DefaultApps.ContainsKey("gate-keeper"), Is.False, "not until it ships");
        Assert.That(EssentialCatalogBootstrapService.MissingEssentials(response with { Unavailable = true }, new HashSet<(string Repo, string Folder)>()), Is.Null);
        Assert.That(EssentialCatalogBootstrapService.MissingEssentials(response with { Stale = true }, new HashSet<(string Repo, string Folder)>()), Is.Null);
    }

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
