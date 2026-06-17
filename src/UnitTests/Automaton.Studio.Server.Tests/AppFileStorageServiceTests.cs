using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using System.Reflection;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Api.Config;

namespace Automaton.Studio.Server.Tests;

[TestFixture]
public class AppFileStorageServiceTests
{
    private static readonly MethodInfo IsBuildBasedFrontendSourcePathMethod =
        typeof(AppFileStorageService).GetMethod("IsBuildBasedFrontendSourcePath", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(AppFileStorageService), "IsBuildBasedFrontendSourcePath");

    [Test]
    public void IsBuildBasedFrontendSourcePath_ReturnsTrue_ForViteStyleSourceTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "package.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "src"));
            Directory.CreateDirectory(Path.Combine(root, "public"));

            var result = InvokeIsBuildBasedFrontendSourcePath(root);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void IsBuildBasedFrontendSourcePath_ReturnsFalse_ForPlainStaticFrontend()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-app-storage-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><title>Static</title>");
            File.WriteAllText(Path.Combine(root, "styles.css"), "body { margin: 0; }");
            File.WriteAllText(Path.Combine(root, "app.js"), "console.log('static');");

            var result = InvokeIsBuildBasedFrontendSourcePath(root);

            Assert.That(result, Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
            WriteFile(
                service.GetAppRuntimeBackendDirectoryPath(srcUserId, srcAppId),
                "server.py",
                "print('runtime secret')");

            await service.CopySourceFilesAsync(srcUserId, srcAppId, dstUserId, dstAppId);

            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "frontend/index.html"), Is.EqualTo("<h1>hello</h1>"));
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "backend/controller.py"), Is.EqualTo("print('hello')"));
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "backend/notes.txt"), Is.EqualTo("safe text"));
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "backend/.env"), Is.Null);
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "backend/secrets.json"), Is.Null);
            Assert.That(await service.ReadSourceFileAsync(dstUserId, dstAppId, "README.md"), Is.Null);
            Assert.That(await service.ReadRuntimeBackendFileAsync(dstUserId, dstAppId, "server.py"), Is.Null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CopyRuntimeFrontendFilesAsync_DoesNotCreateRuntimeBackendCopy()
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

            await service.WriteRuntimeFileAsync(srcUserId, srcAppId, "index.html", "<h1>live</h1>");
            WriteFile(
                service.GetAppRuntimeBackendDirectoryPath(srcUserId, srcAppId),
                "server.py",
                "print('runtime backend')");

            await service.CopyRuntimeFrontendFilesAsync(srcUserId, srcAppId, dstUserId, dstAppId);

            Assert.That(await service.ReadRuntimeFileAsync(dstUserId, dstAppId, "index.html"), Is.EqualTo("<h1>live</h1>"));
            Assert.That(await service.ReadRuntimeBackendFileAsync(dstUserId, dstAppId, "server.py"), Is.Null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool InvokeIsBuildBasedFrontendSourcePath(string path)
    {
        try
        {
            return (bool)IsBuildBasedFrontendSourcePathMethod.Invoke(null, [path])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
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
