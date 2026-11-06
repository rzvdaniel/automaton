using System.IO.Compression;
using System.Net;
using System.Text;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppCatalogTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private const string CatalogJson = """
        {"schemaVersion":1,"repo":"owner/apps","apps":[
          {"id":"backup","name":"Backup","summary":"Copies backups.","category":"Maintenance","folder":"maintenance/backup",
           "commit":"0123456789abcdef0123456789abcdef01234567","minServerVersion":"0.2.31","permissions":["backups:read"]}]}
        """;

    [Test]
    public async Task Get_ReadsTheCatalogAndCachesIt_ThenServesTheCopyWhenTheSourceIsDown()
    {
        await using var db = CreateDb();
        var handler = new FakeHandler(_ => Respond(CatalogJson, "\"v1\""));
        var service = CreateService(db, handler, version: "0.2.31");

        var fresh = await service.GetAsync(Guid.NewGuid(), refresh: true, default);
        handler.Respond = _ => throw new HttpRequestException("blocked");
        var cached = await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.Multiple(() =>
        {
            Assert.That(fresh.Apps.Single().Name, Is.EqualTo("Backup"));
            Assert.That(fresh.Stale, Is.False);
            Assert.That(cached.Apps.Single().Name, Is.EqualTo("Backup"));
            Assert.That(cached.Stale, Is.True);
            Assert.That(cached.Unavailable, Is.False);
            Assert.That(cached.Message, Does.Contain("could not be reached"));
        });
    }

    [Test]
    public async Task Get_ReadsTheCatalogBranchOfTheOfficialRepoByDefault_AndTheConfiguredSourceWhenSet()
    {
        await using var db = CreateDb();
        var urls = new List<string>();
        var handler = new FakeHandler(request => { urls.Add(request.RequestUri!.ToString()); return Respond(CatalogJson, null); });

        await CreateService(db, handler).GetAsync(Guid.NewGuid(), refresh: true, default);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:SourceUrl"] = "http://source.invalid/c.json", ["Catalog:MinimumRefreshSeconds"] = "0" }).Build();
        await new AppCatalogService(db, new FakeFactory(handler), config, NullLogger<AppCatalogService>.Instance).GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.That(urls, Is.EqualTo(new[] { "https://raw.githubusercontent.com/rzvdaniel/automaton-apps/catalog/catalog.json", "http://source.invalid/c.json" }));
    }

    [Test]
    public async Task Get_WithNothingCachedAndTheSourceDown_IsUnavailable()
    {
        await using var db = CreateDb();
        var service = CreateService(db, new FakeHandler(_ => throw new HttpRequestException("blocked")));

        var response = await service.GetAsync(Guid.NewGuid(), refresh: false, default);

        Assert.Multiple(() =>
        {
            Assert.That(response.Unavailable, Is.True);
            Assert.That(response.Apps, Is.Empty);
            Assert.That(response.Message, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Get_SendsTheETagAndKeepsTheCopyOnNotModified()
    {
        await using var db = CreateDb();
        var handler = new FakeHandler(_ => Respond(CatalogJson, "\"v1\""));
        var service = CreateService(db, handler);
        await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        string? sent = null;
        handler.Respond = request =>
        {
            sent = request.Headers.IfNoneMatch.ToString();
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        };
        var response = await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.Multiple(() =>
        {
            Assert.That(sent, Is.EqualTo("\"v1\""));
            Assert.That(response.Apps, Has.Count.EqualTo(1));
            Assert.That(response.Stale, Is.False);
        });
    }

    [Test]
    public async Task Get_RejectsANewerSchemaAndKeepsTheLastGoodCopy()
    {
        await using var db = CreateDb();
        var handler = new FakeHandler(_ => Respond(CatalogJson, null));
        var service = CreateService(db, handler);
        await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        handler.Respond = _ => Respond(CatalogJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"), null);
        var response = await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.Multiple(() =>
        {
            Assert.That(response.Stale, Is.True);
            Assert.That(response.Apps.Single().Name, Is.EqualTo("Backup"));
        });
    }

    [Test]
    public async Task Get_MarksInstalledEntriesForTheirOwnerOnly_AndEntriesNeedingANewerServer()
    {
        await using var db = CreateDb();
        var owner = Guid.NewGuid();
        var app = new WebApp { Id = Guid.NewGuid(), Name = "Backup", InstalledFromRepo = "owner/apps", InstalledFromFolder = "maintenance/backup" };
        db.WebApps.Add(app);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = app.Id, UserId = owner });
        await db.SaveChangesAsync();
        var service = CreateService(db, new FakeHandler(_ => Respond(CatalogJson, null)), version: "server-v0.2.30");

        var mine = await service.GetAsync(owner, refresh: true, default);
        var other = await service.GetAsync(Guid.NewGuid(), refresh: false, default);

        Assert.Multiple(() =>
        {
            Assert.That(mine.Apps.Single().InstalledAppId, Is.EqualTo(app.Id));
            Assert.That(other.Apps.Single().InstalledAppId, Is.Null);
            Assert.That(mine.Apps.Single().RequiresNewerServer, Is.True);
        });
    }

    [Test]
    public async Task Get_ASecondForcedRefreshInsideTheGapUsesTheCopyWithoutCallingGitHub()
    {
        await using var db = CreateDb();
        var calls = 0;
        var handler = new FakeHandler(_ => { calls++; return Respond(CatalogJson, null); });
        var service = new AppCatalogService(db, new FakeFactory(handler), Config(null, "30"), NullLogger<AppCatalogService>.Instance);

        await service.GetAsync(Guid.NewGuid(), refresh: true, default);
        await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Get_SkipsAnInvalidEntryButKeepsTheRest()
    {
        await using var db = CreateDb();
        var bad = "{\"id\":\"bad\",\"name\":\"Bad\",\"summary\":\"s\",\"category\":\"c\",\"folder\":\"../x\",\"commit\":\"" + Commit + "\",\"minServerVersion\":\"0.0.1\",\"permissions\":[]},";
        var body = CatalogJson.Replace("\"apps\":[", "\"apps\":[" + bad);
        var service = CreateService(db, new FakeHandler(_ => Respond(body, null)));

        var response = await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.That(response.Apps.Select(app => app.Id), Is.EqualTo(new[] { "backup" }));
    }

    [TestCase("owner/apps", "maintenance/backup", Commit, true)]
    [TestCase("owner/apps", "aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa/aaaaaaaaaa", Commit, false)]
    [TestCase("owner/apps", "", Commit, true)]
    [TestCase("owner/apps", "../x", Commit, false)]
    [TestCase("owner/apps", "a/./b", Commit, false)]
    [TestCase("owner", "", Commit, false)]
    [TestCase("owner/apps", "", "main", false)]
    [TestCase("owner/apps;rm", "", Commit, false)]
    public void IsValidSource_OnlyAcceptsPinnedGitHubSources(string repo, string folder, string commit, bool valid) =>
        Assert.That(AppCatalogInstallService.IsValidSource(repo, folder, commit), Is.EqualTo(valid));

    [Test]
    public async Task ResolveUrl_PinsTheBranchHeadAndKeepsTheFolder()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/repos/owner/apps/commits/dev"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Commit + "\n") };
        });

        var source = await CreateInstaller(handler).ResolveUrlAsync("https://github.com/owner/apps.git/tree/dev/tools/backup", default);

        Assert.That(source, Is.EqualTo(new Automaton.Api.Models.GitAppSource("owner/apps", "tools/backup", Commit)));
    }

    [TestCase("http://github.com/owner/apps")]
    [TestCase("https://evil.example/owner/apps")]
    [TestCase("https://github.com.evil.example/owner/apps")]
    [TestCase("file:///etc/passwd")]
    public void ResolveUrl_RefusesAnythingButGitHub(string url) =>
        Assert.ThrowsAsync<ArgumentException>(() => CreateInstaller(new FakeHandler(_ => throw new InvalidOperationException())).ResolveUrlAsync(url, default));

    [Test]
    public async Task GetSource_ReadsTheManifestFromTheFolderAtTheCommit()
    {
        var zip = Zip(("apps-" + Commit + "/maintenance/backup/automaton.json",
            """{"name":"Backup","summary":"s","application":{"permissions":["backups:read"],"settings":[{"name":"S3_BUCKET","required":true}]}}"""));
        var handler = new FakeHandler(request =>
        {
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo($"/owner/apps/zip/{Commit}"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        });

        var model = await CreateInstaller(handler).GetSourceAsync(new("owner/apps", "maintenance/backup", Commit), default);

        Assert.Multiple(() =>
        {
            Assert.That(model.Name, Is.EqualTo("Backup"));
            Assert.That(model.Application!.Permissions, Is.EqualTo(new[] { "backups:read" }));
            Assert.That(model.Application.Settings.Single().Name, Is.EqualTo("S3_BUCKET"));
            Assert.That(model.Commit, Is.EqualTo(Commit));
        });
    }

    [Test]
    public void GetSource_WithoutAutomatonJson_IsRefused()
    {
        var zip = Zip(("apps-" + Commit + "/README.md", "hi"));
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });

        var failure = Assert.ThrowsAsync<ArgumentException>(() => CreateInstaller(handler).GetSourceAsync(new("owner/apps", "", Commit), default));

        Assert.That(failure!.Message, Does.Contain("automaton.json"));
    }

    private static HttpResponseMessage Respond(string body, string? etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (etag is not null) response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

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

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static AppCatalogService CreateService(ApplicationDbContext db, FakeHandler handler, string? version = null) =>
        new(db, new FakeFactory(handler), Config(version), NullLogger<AppCatalogService>.Instance);

    private static AppCatalogInstallService CreateInstaller(FakeHandler handler) =>
        new(null!, null!, null!, null!, null!, null!, new FakeFactory(handler), Config(null));

    private static IConfiguration Config(string? version, string refreshGap = "0") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Installation:Version"] = version,
            ["Catalog:MinimumRefreshSeconds"] = refreshGap
        }).Build();

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Respond(request));
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
