using System.IO.Compression;
using System.Net;
using System.Text;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
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
    public async Task Get_FiltersAndPagesOnTheServer_AndCountsTheWholeCatalog()
    {
        await using var db = CreateDb();
        var apps = string.Join(",", Enumerable.Range(0, 30).Select(i =>
            $"{{\"id\":\"a{i}\",\"name\":\"App {i:00}\",\"summary\":\"{(i % 5 == 0 ? "Backs up things" : "Does stuff")}\",\"category\":\"{(i % 3 == 0 ? "Maintenance" : "Tools")}\",\"folder\":\"m/a{i}\",\"commit\":\"{Commit}\",\"minServerVersion\":\"0.0.1\",\"permissions\":[],\"tags\":[\"{(i == 7 ? "Needle" : "plain")}\"]}}"));
        var service = CreateService(db, new FakeHandler(_ => Respond($"{{\"schemaVersion\":1,\"repo\":\"owner/apps\",\"apps\":[{apps}]}}", null)));
        var user = Guid.NewGuid();

        var first = await service.GetAsync(user, true, default, new CatalogQuery { PageSize = 10 });
        var last = await service.GetAsync(user, false, default, new CatalogQuery { PageSize = 10, Page = 99 });
        var search = await service.GetAsync(user, false, default, new CatalogQuery { Q = "backs UP" });
        var tag = await service.GetAsync(user, false, default, new CatalogQuery { Q = "needle" });
        var both = await service.GetAsync(user, false, default, new CatalogQuery { Q = "backs up", Category = "maintenance", Source = "official" });
        var otherSource = await service.GetAsync(user, false, default, new CatalogQuery { Source = "nope" });
        var installed = await service.GetAsync(user, false, default, new CatalogQuery { Installed = true });
        var oversize = await service.GetAsync(user, false, default, new CatalogQuery { PageSize = 5000 });

        Assert.Multiple(() =>
        {
            Assert.That(first.Apps, Has.Count.EqualTo(10));
            Assert.That(first.Apps[0].Name, Is.EqualTo("App 00"));
            Assert.That((first.Total, first.TotalApps, first.Page, first.PageSize), Is.EqualTo((30, 30, 1, 10)));
            Assert.That(last.Page, Is.EqualTo(3));
            Assert.That(last.Apps, Has.Count.EqualTo(10));
            Assert.That(search.Total, Is.EqualTo(6));
            Assert.That(tag.Apps.Single().Name, Is.EqualTo("App 07"));
            Assert.That(both.Apps.Select(a => a.Name), Is.EqualTo(new[] { "App 00", "App 15" }));
            Assert.That(otherSource.Total, Is.EqualTo(0));
            Assert.That(otherSource.TotalApps, Is.EqualTo(30));
            Assert.That(installed.Total, Is.EqualTo(0));
            Assert.That(oversize.PageSize, Is.EqualTo(CatalogQuery.MaximumPageSize));
            Assert.That(first.Categories.Select(c => (c.Value, c.Count)), Is.EqualTo(new[] { ("Maintenance", 10), ("Tools", 20) }));
            Assert.That(first.Sources!.Single().Count, Is.EqualTo(30));
        });
    }

    [Test]
    public async Task Get_WithNullFieldsInAnEntry_StillListsAndSearchesTheCatalog()
    {
        await using var db = CreateDb();
        var json = $"{{\"schemaVersion\":1,\"repo\":\"owner/apps\",\"apps\":[{{\"id\":\"x\",\"name\":\"Odd\",\"summary\":null,\"category\":null,\"folder\":\"m/x\",\"commit\":\"{Commit}\",\"tags\":[null,\"ok\"]}}]}}";
        var service = CreateService(db, new FakeHandler(_ => Respond(json, null)));

        var all = await service.GetAsync(Guid.NewGuid(), true, default, new CatalogQuery { Q = "ok" });

        Assert.Multiple(() =>
        {
            Assert.That(all.Apps.Single().Name, Is.EqualTo("Odd"));
            Assert.That(all.Categories, Is.Empty);
        });
    }

    [Test]
    public async Task Get_ReadsTheMainBranchOfTheOfficialRepoByDefault_AndTheConfiguredSourceWhenSet()
    {
        await using var db = CreateDb();
        var urls = new List<string>();
        var handler = new FakeHandler(request => { urls.Add(request.RequestUri!.ToString()); return Respond(CatalogJson, null); });

        await CreateService(db, handler).GetAsync(Guid.NewGuid(), refresh: true, default);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Catalog:SourceUrl"] = "http://source.invalid/c.json", ["Catalog:MinimumRefreshSeconds"] = "0" }).Build();
        await new AppCatalogService(db, new FakeFactory(handler), config, NullLogger<AppCatalogService>.Instance, new CatalogSourceStore(db, new FakeProtector())).GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.That(urls, Is.EqualTo(new[] { "https://raw.githubusercontent.com/rzvdaniel/automaton-apps/main/catalog.json", "http://source.invalid/c.json" }));
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
        var mineOnly = await service.GetAsync(owner, false, default, new CatalogQuery { Installed = true });
        var otherOnly = await service.GetAsync(Guid.NewGuid(), false, default, new CatalogQuery { Installed = true });

        Assert.Multiple(() =>
        {
            Assert.That(mineOnly.Apps.Single().InstalledAppId, Is.EqualTo(app.Id));
            Assert.That(otherOnly.Total, Is.EqualTo(0));
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
        var service = new AppCatalogService(db, new FakeFactory(handler), Config(null, "30"), NullLogger<AppCatalogService>.Instance, new CatalogSourceStore(db, new FakeProtector()));

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
    public async Task GetSource_GivesAnEmptyDescription_WhenTheManifestHasNone()
    {
        // WebApps.Description is NOT NULL: a manifest without a description (the catalog init template) must not install as null.
        var zip = Zip(("apps-" + Commit + "/hello/automaton.json", """{"name":"Hello","summary":"s","application":{}}"""));
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });

        var model = await CreateInstaller(handler).GetSourceAsync(new("owner/apps", "hello", Commit), default);

        Assert.That(model.Description, Is.EqualTo(string.Empty));
    }

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

    // --- custom catalog sources ---

    private const string Token = "github_pat_secret123";
    private const string Other = "{\"schemaVersion\":1,\"repo\":\"someone/else\",\"apps\":[{\"id\":\"tool\",\"name\":\"Tool\",\"summary\":\"s\",\"category\":\"Ops\",\"folder\":\"tool\",\"commit\":\"" + Commit + "\",\"minServerVersion\":\"0.0.1\",\"permissions\":[]}]}";

    private static async Task<(AppCatalogService Service, CatalogSourceStore Store, ApplicationDbContext Db)> WithSourcesAsync(FakeHandler handler)
    {
        var db = CreateDb();
        var store = new CatalogSourceStore(db, new FakeProtector());
        return (new AppCatalogService(db, new FakeFactory(handler), Config(null), NullLogger<AppCatalogService>.Instance, store), store, db);
    }

    [Test]
    public async Task APublicCustomSource_IsReadFromItsMainBranchByDefault_LabelledWithItsName_AndPinnedToItsOwnRepository()
    {
        var urls = new List<string>();
        var handler = new FakeHandler(request =>
        {
            urls.Add(request.RequestUri!.ToString());
            Assert.That(request.Headers.Authorization, Is.Null);
            return Respond(request.RequestUri!.Host == "raw.githubusercontent.com" && request.RequestUri.AbsolutePath.StartsWith("/someone/") ? Other : CatalogJson, null);
        });
        var (service, store, db) = await WithSourcesAsync(handler);
        await using var _ = db;
        var candidate = CatalogSourceStore.Candidate("https://github.com/someone/apps.git", null, "Community store", null);
        Assert.That(await service.ProbeAsync(candidate, default), Is.Null);
        await store.AddAsync(candidate, Guid.NewGuid(), default);

        var response = await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.Multiple(() =>
        {
            Assert.That(urls, Does.Contain("https://raw.githubusercontent.com/someone/apps/main/catalog.json"));
            Assert.That(response.Apps.Select(a => (a.Name, a.SourceName, a.CustomSource)), Is.EqualTo(new[] { ("Backup", "Official", false), ("Tool", "Community store", true) }));
            // The file says "someone/else", but the entry must install from the source's own repository.
            Assert.That(response.Apps.Last().Repo, Is.EqualTo("someone/apps"));
        });
    }

    [Test]
    public async Task APrivateSource_IsReadWithTheTokenOnTheServer_AndTheTokenIsStoredProtectedAndNeverListed()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/repos/acme/apps/contents/catalog.json"));
                Assert.That(request.Headers.Authorization!.ToString(), Is.EqualTo("Bearer " + Token));
                return Respond(Other, null);
            }
            return Respond(CatalogJson, null);
        });
        var (service, store, db) = await WithSourcesAsync(handler);
        await using var _ = db;
        var candidate = CatalogSourceStore.Candidate("acme/apps", "main", null, Token);
        Assert.That(await service.ProbeAsync(candidate, default), Is.Null);
        await store.AddAsync(candidate, Guid.NewGuid(), default);

        var listed = System.Text.Json.JsonSerializer.Serialize(await store.ListAsync(default));
        var stored = string.Concat(db.Settings.AsNoTracking().Select(x => x.Value));
        var response = await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.Multiple(() =>
        {
            Assert.That(listed, Does.Not.Contain(Token));
            Assert.That(listed, Does.Contain("\"HasToken\":true"));
            Assert.That(stored, Does.Not.Contain(Token));
            Assert.That(stored, Does.Contain("enc:"));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(response), Does.Not.Contain(Token));
            Assert.That(response.Apps.Select(a => a.Name), Is.EquivalentTo(new[] { "Backup", "Tool" }));
        });
    }

    [Test]
    public async Task TheToken_IsOnlyHandedOutForAnAppThatSourceListsAtThatFolderAndCommit()
    {
        var (service, store, db) = await WithSourcesAsync(new FakeHandler(request => Respond(request.RequestUri!.Host == "api.github.com" ? Other : CatalogJson, null)));
        await using var _ = db;
        await store.AddAsync(CatalogSourceStore.Candidate("acme/apps", null, null, Token), Guid.NewGuid(), default);
        await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.Multiple(async () =>
        {
            Assert.That(await service.TokenForAsync("acme/apps", "tool", Commit, default), Is.EqualTo(Token));
            Assert.That(await service.TokenForAsync("ACME/apps", "tool", Commit, default), Is.EqualTo(Token));
            Assert.That(await service.TokenForAsync("acme/apps", "secret-folder", Commit, default), Is.Null);
            Assert.That(await service.TokenForAsync("acme/apps", "tool", new string('a', 40), default), Is.Null);
            Assert.That(await service.TokenForAsync("owner/apps", "maintenance/backup", Commit, default), Is.Null);
        });
    }

    [Test]
    public async Task AFolderTheSourceDoesNotList_NeverGetsTheToken()
    {
        var zip = Zip(("acme-apps-" + Commit + "/other-folder/automaton.json", "{\"name\":\"X\"}"));
        var auths = new List<string?>();
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/zip")) { auths.Add(request.Headers.Authorization?.ToString()); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }; }
            return Respond(request.RequestUri.Host == "api.github.com" ? Other : CatalogJson, null);
        });
        var (service, store, db) = await WithSourcesAsync(handler);
        await using var _ = db;
        await store.AddAsync(CatalogSourceStore.Candidate("acme/apps", null, null, Token), Guid.NewGuid(), default);
        await service.GetAsync(Guid.NewGuid(), refresh: true, default);
        var installer = new AppCatalogInstallService(null!, null!, null!, null!, null!, null!, new FakeFactory(handler), Config(null), service);

        await installer.GetSourceAsync(new("acme/apps", "other-folder", Commit), default);

        Assert.That(auths, Is.EqualTo(new string?[] { null }));
    }

    [Test]
    public async Task ThePrivateDownload_UsesTheZipballEndpointWithTheBearerToken()
    {
        var zip = Zip(("acme-apps-" + Commit + "/tool/automaton.json", "{\"name\":\"Tool\"}"));
        string? auth = null; string? path = null;
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("zipball")) { auth = request.Headers.Authorization?.ToString(); path = request.RequestUri.AbsolutePath; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }; }
            return Respond(request.RequestUri.Host == "api.github.com" ? Other : CatalogJson, null);
        });
        var (service, store, db) = await WithSourcesAsync(handler);
        await using var _ = db;
        await store.AddAsync(CatalogSourceStore.Candidate("acme/apps", null, null, Token), Guid.NewGuid(), default);
        await service.GetAsync(Guid.NewGuid(), refresh: true, default);
        var installer = new AppCatalogInstallService(null!, null!, null!, null!, null!, null!, new FakeFactory(handler), Config(null), service);

        var model = await installer.GetSourceAsync(new("acme/apps", "tool", Commit), default);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo($"/repos/acme/apps/zipball/{Commit}"));
            Assert.That(auth, Is.EqualTo("Bearer " + Token));
            Assert.That(model.Name, Is.EqualTo("Tool"));
        });
    }

    [Test]
    public async Task RemovingASource_DropsItsEntriesAndSavedCopy_ButNotAppsInstalledFromIt()
    {
        var (service, store, db) = await WithSourcesAsync(new FakeHandler(request => Respond(request.RequestUri!.AbsolutePath.StartsWith("/someone/") ? Other : CatalogJson, null)));
        await using var _ = db;
        var added = await store.AddAsync(CatalogSourceStore.Candidate("someone/apps", null, null, null), Guid.NewGuid(), default);
        var owner = Guid.NewGuid();
        var app = new WebApp { Id = Guid.NewGuid(), Name = "Tool", InstalledFromRepo = "someone/apps", InstalledFromFolder = "tool" };
        db.WebApps.Add(app);
        db.WebAppUsers.Add(new WebAppUser { WebAppId = app.Id, UserId = owner });
        await db.SaveChangesAsync();
        Assert.That((await service.GetAsync(owner, refresh: true, default)).Apps.Select(a => a.Name), Does.Contain("Tool"));

        await store.RemoveAsync(added.Id, default);
        var after = await service.GetAsync(owner, refresh: false, default);

        Assert.Multiple(() =>
        {
            Assert.That(after.Apps.Select(a => a.Name), Is.EqualTo(new[] { "Backup" }));
            Assert.That(db.Settings.Any(x => x.Key == AppCatalogService.CacheKeyFor(added.Id)), Is.False);
            Assert.That(db.WebApps.Any(x => x.Id == app.Id), Is.True);
        });
    }

    [Test]
    public async Task TheOfficialSource_CanBeTurnedOff_AndAnUnknownSourceIsNotFound()
    {
        var (service, store, db) = await WithSourcesAsync(new FakeHandler(_ => Respond(CatalogJson, null)));
        await using var _ = db;

        await store.SetEnabledAsync(CatalogSourceStore.OfficialId, false, default);
        var response = await service.GetAsync(Guid.NewGuid(), refresh: true, default);

        Assert.Multiple(async () =>
        {
            Assert.That(response.Apps, Is.Empty);
            Assert.That(response.Message, Does.Contain("No catalog source"));
            Assert.That(await store.SetEnabledAsync("nope", true, default), Is.Null);
            Assert.That(await store.RemoveAsync("nope", default), Is.Null);
        });
    }

    [Test]
    public async Task AddingTheSameRepositoryTwice_OrTheOfficialOne_IsRefused()
    {
        var (_, store, db) = await WithSourcesAsync(new FakeHandler(_ => Respond(CatalogJson, null)));
        await using var __ = db;
        await store.AddAsync(CatalogSourceStore.Candidate("acme/apps", null, null, null), Guid.NewGuid(), default);

        Assert.ThrowsAsync<ArgumentException>(() => store.EnsureAddableAsync(CatalogSourceStore.Candidate("ACME/apps", null, null, null), default));
        Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(CatalogSourceStore.Candidate("ACME/apps", null, null, null), Guid.NewGuid(), default));
        Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(CatalogSourceStore.Candidate("rzvdaniel/automaton-apps", null, null, null), Guid.NewGuid(), default));
    }

    [TestCase("acme")]
    [TestCase("acme/apps/extra")]
    [TestCase("https://gitlab.com/acme/apps")]
    [TestCase("https://github.com.evil.example/acme/apps")]
    [TestCase("acme/apps;rm")]
    [TestCase("acme/..")]
    [TestCase("../apps")]
    public void Candidate_RefusesAnythingButAGitHubRepository(string address) =>
        Assert.Throws<ArgumentException>(() => CatalogSourceStore.Candidate(address, null, null, null));

    [TestCase("../x")]
    [TestCase("a..b")]
    [TestCase("a/../b")]
    [TestCase(".")]
    [TestCase("main branch")]
    public void Candidate_RefusesAnUnsafeBranch(string branch) =>
        Assert.Throws<ArgumentException>(() => CatalogSourceStore.Candidate("acme/apps", branch, null, null));

    [Test]
    public async Task ASourceThatCannotBeRead_IsNotAdded_AndSaysWhy()
    {
        var (service, _, db) = await WithSourcesAsync(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        await using var _ = db;

        var problem = await service.ProbeAsync(CatalogSourceStore.Candidate("acme/missing", null, null, null), default);

        Assert.That(problem, Does.Contain("404").And.Contain("catalog.json"));
    }

    [Test]
    public void ManagingSources_NeedsASystemAdministrator()
    {
        var controller = typeof(Automaton.Api.Controllers.Admin.AdminCatalogSourcesController);
        var authorize = controller.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single();
        Assert.That(authorize.Roles, Is.EqualTo(Automaton.Api.Enums.UserRole.SystemAdministrator));
        Assert.That(controller.GetMethods().Where(m => m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true).Any()), Is.Empty);
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
        new(db, new FakeFactory(handler), Config(version), NullLogger<AppCatalogService>.Instance, new CatalogSourceStore(db, new FakeProtector()));

    private static AppCatalogInstallService CreateInstaller(FakeHandler handler) =>
        new(null!, null!, null!, null!, null!, null!, new FakeFactory(handler), Config(null), CreateService(CreateDb(), handler));

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

    private sealed class FakeProtector : Automaton.Api.Services.Interfaces.ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        public string Unprotect(string protectedValue) => Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue["enc:".Length..]));
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
