using System.IO.Compression;
using Automaton.Api.Models;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppArchiveExportSafetyTests
{
    [Test]
    public async Task ExportAsync_ExcludesConfiguredSecretPrivateAndRuntimePaths()
    {
        var entries = await ExportEntryNamesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(entries, Does.Contain("README.md"));
            Assert.That(entries, Does.Not.Contain("backend/.env"));
            Assert.That(entries, Does.Not.Contain("backend/secrets.json"));
            Assert.That(entries, Does.Not.Contain("backend/private/token.json"));
            Assert.That(entries, Does.Not.Contain("backend/data/app-data.json"));
            Assert.That(entries, Does.Not.Contain("backend/.git/config"));
        });
    }

    private static async Task<string[]> ExportEntryNamesAsync()
    {
        await using var fixture = await ArchiveFixture.CreateAsync();
        var result = await fixture.ArchiveService.ExportAsync(fixture.AppId, CancellationToken.None);
        using var bytes = new MemoryStream(result!.Content);
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private sealed class ArchiveFixture : IAsyncDisposable
    {
        private ArchiveFixture(
            Automaton.Api.Data.ApplicationDbContext db,
            AppArchiveService archiveService,
            Guid appId,
            string root)
        {
            Db = db;
            ArchiveService = archiveService;
            AppId = appId;
            Root = root;
        }

        private Automaton.Api.Data.ApplicationDbContext Db { get; }
        public AppArchiveService ArchiveService { get; }
        public Guid AppId { get; }
        private string Root { get; }

        public static async Task<ArchiveFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "automaton-app-archive-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var db = AppAuthorizationTestData.CreateDbContext();
            var userId = Guid.Empty;
            var appId = Guid.NewGuid();
            AppAuthorizationTestData.SeedApp(db, appId, userId, "archive-export");
            var apps = AppAuthorizationTestServiceFactory.CreateAppsService(db, userId, root, out var storage);
            await AddSourceFilesAsync(storage, userId, appId);
            var archiveService = new AppArchiveService(
                apps,
                storage,
                AppAuthorizationTestData.CreateUserContextService(userId));
            return new ArchiveFixture(db, archiveService, appId, root);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private static async Task AddSourceFilesAsync(AppFileStorageService storage, Guid userId, Guid appId)
    {
        await storage.WriteSourceFileAsync(userId, appId, "README.md", "portable source");
        var environmentFile = Path.Combine(storage.GetAppSourceDirectoryPath(userId, appId), "backend", ".env");
        Directory.CreateDirectory(Path.GetDirectoryName(environmentFile)!);
        await File.WriteAllTextAsync(environmentFile, "TOKEN=secret");
        await storage.WriteSourceFileAsync(userId, appId, "backend/secrets.json", "{\"key\":\"secret\"}");
        await storage.WriteSourceFileAsync(userId, appId, "backend/private/token.json", "private");
        await storage.WriteSourceFileAsync(userId, appId, "backend/data/app-data.json", "private data");
        await storage.WriteSourceFileAsync(userId, appId, "backend/.git/config", "remote=private");
    }
}
