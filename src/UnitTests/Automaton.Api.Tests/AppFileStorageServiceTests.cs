using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Config;
using Automaton.Api.Constants;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppFileStorageServiceTests
{
    [Test]
    public void DirectFileEditing_AllowsNormalSourceExtensions()
    {
        Assert.DoesNotThrow(() => AppFileStorageService.ValidateFileExtensionPublic("Program.cs"));
    }

    [Test]
    public async Task CopySourceFilesAsync_CopiesOnlyPortableSourceDirectories_AndSkipsSecrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var srcUserId = Guid.NewGuid();
        var srcAppId = Guid.NewGuid();
        var dstUserId = Guid.NewGuid();
        var dstAppId = Guid.NewGuid();

        try
        {
            var service = CreateStorageService(root);

            await service.WriteSourceFileAsync(srcUserId, srcAppId, "frontend/index.html", "<h1>hello</h1>");
            await service.WriteSourceFileAsync(srcUserId, srcAppId, "backend/controller.py", "print('hello')");
            await service.WriteSourceFileAsync(srcUserId, srcAppId, "backend/notes.txt", "safe text");
            WriteFile(
                service.GetAppSourceDirectoryPath(srcUserId, srcAppId),
                "backend/.env",
                "TOKEN=secret");
            await service.WriteSourceFileAsync(srcUserId, srcAppId, "backend/secrets.json", "{\"token\":\"secret\"}");
            await service.WriteSourceFileAsync(srcUserId, srcAppId, "README.md", "do not remix root files");
            await service.CopySourceFilesAsync(srcUserId, srcAppId, dstUserId, dstAppId);

            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "frontend/index.html"), Is.EqualTo("<h1>hello</h1>"));
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "backend/controller.py"), Is.EqualTo("print('hello')"));
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "backend/notes.txt"), Is.EqualTo("safe text"));
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "backend/.env"), Is.Null);
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "backend/secrets.json"), Is.Null);
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "README.md"), Is.EqualTo("do not remix root files"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task WriteRuntimeFileAsync_AllowsWebManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var service = CreateStorageService(root);
            var userId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            const string manifest = """{"name":"Test App","start_url":"./"}""";

            await service.WriteRuntimeFileAsync(userId, appId, "site.webmanifest", manifest);

            Assert.That(await service.ReadRuntimeFileAsync(userId, appId, "site.webmanifest"), Is.EqualTo(manifest));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task GetAppStorageMetadataAsync_MigratesLegacyNestedMetadataToGenericSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = CreateStorageService(root);
            var userId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var appRoot = service.GetAppRootPath(userId, appId);
            Directory.CreateDirectory(Path.Combine(appRoot, "source"));
            File.WriteAllText(Path.Combine(appRoot, "source", "Program.cs"), "class Program {}");
            File.WriteAllText(Path.Combine(appRoot, "_metadata.json"), """
                {"Version":3,"DevelopmentMode":"Studio","Frontend":{"SourceAvailable":true,"SourceModifiedAtUtc":"2026-01-01T00:00:00Z","RuntimeSource":"CliBuild","RuntimeAvailable":true,"RuntimeDeployedAtUtc":"2026-01-02T00:00:00Z"},"Backend":{"SourceAvailable":false,"SourceModifiedAtUtc":"2026-01-01T00:00:00Z","RuntimeSource":"Studio","RuntimeAvailable":false,"RuntimeDeployedAtUtc":null},"ConnectedFlowIds":["flow-1"],"Settings":{"theme":"dark"},"GitHub":{"repository":"owner/repo"}}
                """);

            var metadata = await service.GetAppStorageMetadataAsync(userId, appId);

            Assert.That(metadata.MetadataSchemaVersion, Is.EqualTo(2));
            Assert.That(metadata.SourceAvailable, Is.True);
            Assert.That(metadata.ConnectedFlowIds, Is.EqualTo(new[] { "flow-1" }));
            Assert.That(metadata.RuntimeSource, Is.EqualTo(AppRuntimeSources.CliBuild));
            Assert.That(File.ReadAllText(Path.Combine(appRoot, "_metadata.json")), Does.Contain("MetadataSchemaVersion"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("CliBuild", "Studio", true, false, "cli-build")]
    [TestCase("Studio", "CliDeploy", false, true, "cli-deploy")]
    public async Task GetAppStorageMetadataAsync_UsesOnlyAvailableLegacyRuntimeComponent(
        string frontendSource, string backendSource, bool frontendAvailable, bool backendAvailable, string expectedSource)
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = CreateStorageService(root);
            var userId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var appRoot = service.GetAppRootPath(userId, appId);
            Directory.CreateDirectory(Path.Combine(appRoot, "source"));
            File.WriteAllText(Path.Combine(appRoot, "_metadata.json"), JsonSerializer.Serialize(new
            {
                Version = 1,
                DevelopmentMode = "Studio",
                Frontend = new { SourceAvailable = true, SourceModifiedAtUtc = "2026-01-01T00:00:00Z", RuntimeSource = frontendSource, RuntimeAvailable = frontendAvailable, RuntimeDeployedAtUtc = (string?)null },
                Backend = new { SourceAvailable = true, SourceModifiedAtUtc = "2026-01-01T00:00:00Z", RuntimeSource = backendSource, RuntimeAvailable = backendAvailable, RuntimeDeployedAtUtc = (string?)null },
                ConnectedFlowIds = new[] { "flow-1" },
                Settings = new { theme = "dark" },
                GitHub = new { repository = "owner/repo" }
            }));

            var metadata = await service.GetAppStorageMetadataAsync(userId, appId);

            Assert.That(metadata.RuntimeSource, Is.EqualTo(expectedSource));
            Assert.That(metadata.ConnectedFlowIds, Is.EqualTo(new[] { "flow-1" }));
            Assert.That(File.ReadAllText(Path.Combine(appRoot, "_metadata.json")), Does.Contain("\"MetadataSchemaVersion\": 2"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task GetAppStorageMetadataAsync_PrefersNewestLegacyDeploymentTimestamp()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = CreateStorageService(root);
            var userId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var appRoot = service.GetAppRootPath(userId, appId);
            Directory.CreateDirectory(Path.Combine(appRoot, "source"));
            File.WriteAllText(Path.Combine(appRoot, "_metadata.json"), """
                {"Version":1,"DevelopmentMode":"Studio","Frontend":{"SourceAvailable":true,"SourceModifiedAtUtc":"2026-01-01T00:00:00Z","RuntimeSource":"CliBuild","RuntimeAvailable":true,"RuntimeDeployedAtUtc":"2026-01-03T00:00:00Z"},"Backend":{"SourceAvailable":true,"SourceModifiedAtUtc":"2026-01-01T00:00:00Z","RuntimeSource":"CliDeploy","RuntimeAvailable":true,"RuntimeDeployedAtUtc":"2026-01-02T00:00:00Z"},"ConnectedFlowIds":[],"Settings":null,"GitHub":null}
                """);

            var metadata = await service.GetAppStorageMetadataAsync(userId, appId);
            Assert.That(metadata.RuntimeSource, Is.EqualTo(AppRuntimeSources.CliBuild));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task GetAppStorageMetadataAsync_UsesDeterministicFallbackWhenNothingWasDeployed()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = CreateStorageService(root);
            var userId = Guid.NewGuid();
            var appId = Guid.NewGuid();
            var appRoot = service.GetAppRootPath(userId, appId);
            Directory.CreateDirectory(Path.Combine(appRoot, "source"));
            File.WriteAllText(Path.Combine(appRoot, "_metadata.json"), """
                {"Version":1,"DevelopmentMode":"Studio","Frontend":{"SourceAvailable":true,"SourceModifiedAtUtc":null,"RuntimeSource":"Studio","RuntimeAvailable":false,"RuntimeDeployedAtUtc":null},"Backend":{"SourceAvailable":false,"SourceModifiedAtUtc":null,"RuntimeSource":"Studio","RuntimeAvailable":false,"RuntimeDeployedAtUtc":null},"ConnectedFlowIds":[],"Settings":null,"GitHub":null}
                """);

            var metadata = await service.GetAppStorageMetadataAsync(userId, appId);
            Assert.That(metadata.RuntimeSource, Is.EqualTo(AppRuntimeSources.Studio));
            Assert.That(metadata.RuntimeAvailable, Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreVersionAsync_RestoresNestedFilesSnapshotIntoSourceOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        try
        {
            var service = CreateStorageService(root);

            await service.WriteSourceFileAsync(userId, appId, "frontend/index.html", "<h1>current</h1>");
            await service.WriteSourceFileAsync(userId, appId, "backend/controller.py", "print('current')");
            await service.WriteRuntimeFileAsync(userId, appId, "index.html", "<h1>live</h1>");

            await service.CreateVersionSnapshotAsync(userId, appId, 1);

            await service.WriteSourceFileAsync(userId, appId, "frontend/index.html", "<h1>changed</h1>");
            await service.DeleteSourceFileAsync(userId, appId, "backend/controller.py");

            await service.RestoreVersionAsync(userId, appId, 1);

            Assert.That(await service.ReadSourceFileAsync(userId, appId, "frontend/index.html"), Is.EqualTo("<h1>current</h1>"));
            Assert.That(await service.ReadSourceFileAsync(userId, appId, "backend/controller.py"), Is.EqualTo("print('current')"));
            Assert.That(await service.ReadRuntimeFileAsync(userId, appId, "index.html"), Is.EqualTo("<h1>live</h1>"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RestoreVersionAsync_RestoresLegacyFlatSnapshotFormat()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var userId = Guid.NewGuid();
        var appId = Guid.NewGuid();

        try
        {
            var service = CreateStorageService(root);
            await service.WriteSourceFileAsync(userId, appId, "index.html", "<h1>current</h1>");
            await service.WriteSourceFileAsync(userId, appId, "frontend/ignored.html", "<h1>ignored</h1>");

            var legacyVersionPath = Path.Combine(service.GetAppVersionDirectoryPath(userId, appId), "v001");
            Directory.CreateDirectory(legacyVersionPath);
            File.WriteAllText(Path.Combine(legacyVersionPath, "index.html"), "<h1>legacy</h1>");
            Directory.CreateDirectory(Path.Combine(legacyVersionPath, "backend"));
            File.WriteAllText(Path.Combine(legacyVersionPath, "backend", "controller.py"), "print('legacy')");

            await service.RestoreVersionAsync(userId, appId, 1);

            Assert.That(await service.ReadSourceFileAsync(userId, appId, "index.html"), Is.EqualTo("<h1>legacy</h1>"));
            Assert.That(await service.ReadSourceFileAsync(userId, appId, "backend/controller.py"), Is.EqualTo("print('legacy')"));
            Assert.That(await service.ReadSourceFileAsync(userId, appId, "frontend/ignored.html"), Is.Null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AppFileStorageService CreateStorageService(string contentRootPath)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var dbContext = new ApplicationDbContext(dbOptions);
        var userContext = new UserContextService(new HttpContextAccessor());
        var settingsService = new SettingsService(
            dbContext,
            userContext,
            Options.Create(new AppFileRuleBaselineOptions
            {
                RemixSourceExclude = [".env", ".env.*", "secrets.json", "secrets", "secrets/**"],
                RemixFrontendExclude = []
            }));

        return new AppFileStorageService(new StubWebHostEnvironment(contentRootPath), settingsService);
    }

    private static void WriteFile(string rootPath, string relativePath, string content)
    {
        var filePath = Path.Combine(rootPath, relativePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, content);
    }

    private sealed class StubWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
