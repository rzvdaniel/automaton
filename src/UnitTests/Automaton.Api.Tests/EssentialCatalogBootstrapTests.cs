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
    public void BootstrapSelectsOnlyMissingOfficialEntriesAtTheirCatalogCommits()
    {
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var entries = EssentialCatalogBootstrapService.AppIds.Select(id => Entry(id, commit)).ToList();
        entries.Add(Entry("server-console", "ffffffffffffffffffffffffffffffffffffffff", source: "custom"));
        var response = new CatalogResponse(entries, DateTime.UtcNow, false, false, null);
        var installed = new HashSet<(string Repo, string Folder)> { ("rzvdaniel/automaton-apps", "maintenance/heartbeat") };

        var missing = EssentialCatalogBootstrapService.MissingEssentials(response, installed);

        Assert.That(missing!.Select(app => (app.Id, app.Commit)), Is.EqualTo(new[]
        {
            ("server-console", commit), ("website-monitor", commit)
        }));
        Assert.That(EssentialCatalogBootstrapService.MissingEssentials(response, entries.Take(3)
            .Select(app => (app.Repo, app.Folder)).ToHashSet()), Is.Empty);
        Assert.That(EssentialCatalogBootstrapService.MissingEssentials(response with { Unavailable = true }, installed), Is.Null);
        Assert.That(EssentialCatalogBootstrapService.MissingEssentials(response with { Stale = true }, installed), Is.Null);
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
