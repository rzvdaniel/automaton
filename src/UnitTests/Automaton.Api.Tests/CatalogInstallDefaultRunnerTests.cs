using System.IO.Compression;
using System.Net;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public class CatalogInstallDefaultRunnerTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public async Task DefaultRunner_IsTheFirstOnlineApplicationRunnerTheUserCanSee()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        AddRunner(db, user, "a-offline", online: false);
        AddRunner(db, Guid.NewGuid(), "b-someone-elses", online: true);
        var expected = AddRunner(db, user, "c-online", online: true);
        AddRunner(db, user, "d-online", online: true);
        await db.SaveChangesAsync();

        Assert.That(await Policy(db, user).FindDefaultRunnerIdAsync(user), Is.EqualTo(expected));
    }

    [Test]
    public async Task DefaultRunner_IsNullWhenNoRunnerIsOnlineOrUsable()
    {
        await using var db = CreateDb();
        var user = Guid.NewGuid();
        AddRunner(db, user, "offline", online: false);
        AddRunner(db, user, "removing", online: true, removing: true);
        await db.SaveChangesAsync();

        Assert.That(await Policy(db, user).FindDefaultRunnerIdAsync(user), Is.Null);
    }

    [Test]
    public async Task Install_WithoutARunnerAndNoneAvailable_IsRefusedWithAClearMessageAndCreatesNothing()
    {
        await using var db = CreateDb();
        var installer = Installer(db, Guid.NewGuid());

        var failure = Assert.ThrowsAsync<ArgumentException>(() =>
            installer.InstallAsync(new CatalogInstallRequest { Repo = "owner/apps", Folder = "tool", Commit = Commit }, default));

        Assert.That(failure!.Message, Does.Contain("No online Runner").And.Contain("Nothing was installed"));
        Assert.That(await db.WebApps.CountAsync(), Is.Zero);
    }

    private static Guid AddRunner(ApplicationDbContext db, Guid userId, string name, bool online, bool removing = false)
    {
        var runner = new Runner
        {
            Id = Guid.NewGuid(), Name = name, Purpose = RunnerPurposes.Managed,
            ApplicationProfile = RunnerApplicationProfiles.MachineManagement,
            CapabilitiesJson = RunnerCapabilities.Serialize(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed), RunnerPurposes.Managed),
            Status = online ? "Online" : "Offline",
            DeletionRequestedAtUtc = removing ? DateTime.UtcNow : null
        };
        db.Runners.Add(runner);
        db.RunnerUsers.Add(new RunnerUser { RunnerId = runner.Id, UserId = userId });
        return runner.Id;
    }

    private static AppRunnerAuthorizationPolicy Policy(ApplicationDbContext db, Guid userId) =>
        new(db, new ResourceAuthorizationService(db, AppAuthorizationTestData.CreateUserContextService(userId)));

    private static AppCatalogInstallService Installer(ApplicationDbContext db, Guid userId)
    {
        var handler = new Handler(Zip(("acme-" + Commit + "/tool/automaton.json", "{\"name\":\"Tool\"}")));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:MinimumRefreshSeconds"] = "0" }).Build();
        var factory = new Factory(handler);
        var catalog = new AppCatalogService(db, factory, config, NullLogger<AppCatalogService>.Instance, new CatalogSourceStore(db, null!));
        var userContext = AppAuthorizationTestData.CreateUserContextService(userId);
        var assignment = new AppRunnerAssignmentPolicy(db, Policy(db, userId), null!);
        return new AppCatalogInstallService(db, userContext, null!, assignment, null!, null!, factory, config, catalog);
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static byte[] Zip(params (string Path, string Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        return stream.ToArray();
    }

    private sealed class Handler(byte[] zip) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
