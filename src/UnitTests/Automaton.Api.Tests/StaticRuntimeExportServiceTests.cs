using System.IO.Compression;
using Automaton.Api.Config;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public class StaticRuntimeExportServiceTests
{
    [Test]
    public async Task ExportFromDirectoryAsync_CopiesFrontendFilesToAppCache()
    {
        using var tempRoot = new TemporaryDirectory();
        var appId = Guid.NewGuid();
        var sourcePath = Path.Combine(tempRoot.Path, "runtime", "frontend");
        Directory.CreateDirectory(Path.Combine(sourcePath, "assets"));
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "index.html"), "<!doctype html><html></html>");
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "assets", "app.js"), "console.log('ok');");

        var service = CreateService(tempRoot.Path);

        await service.ExportFromDirectoryAsync(appId, sourcePath, CancellationToken.None);

        var targetPath = Path.Combine(tempRoot.Path, "static-runtime", "apps", appId.ToString());
        Assert.That(File.Exists(Path.Combine(targetPath, "index.html")), Is.True);
        Assert.That(File.Exists(Path.Combine(targetPath, "assets", "app.js")), Is.True);
    }

    [Test]
    public void ExportFromDirectoryAsync_RejectsUnsafeFiles()
    {
        using var tempRoot = new TemporaryDirectory();
        var appId = Guid.NewGuid();
        var sourcePath = Path.Combine(tempRoot.Path, "runtime", "frontend");
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(Path.Combine(sourcePath, "index.html"), "<!doctype html><html></html>");
        File.WriteAllText(Path.Combine(sourcePath, ".env"), "secret");

        var service = CreateService(tempRoot.Path);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ExportFromDirectoryAsync(appId, sourcePath, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("not safe for static runtime hosting"));
    }

    [Test]
    public async Task ExportFromDirectoryAsync_PreservesLastKnownGoodCacheWhenExportFails()
    {
        using var tempRoot = new TemporaryDirectory();
        var appId = Guid.NewGuid();
        var sourcePath = Path.Combine(tempRoot.Path, "runtime", "frontend");
        Directory.CreateDirectory(sourcePath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "index.html"), "<!doctype html><html>good</html>");

        var service = CreateService(tempRoot.Path);
        await service.ExportFromDirectoryAsync(appId, sourcePath, CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(sourcePath, ".env"), "secret");

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.ExportFromDirectoryAsync(appId, sourcePath, CancellationToken.None));

        var targetIndexPath = Path.Combine(tempRoot.Path, "static-runtime", "apps", appId.ToString(), "index.html");
        Assert.That(await File.ReadAllTextAsync(targetIndexPath), Does.Contain("good"));
    }

    [Test]
    public async Task ExportFromDeployPackageAsync_CopiesRuntimeFrontendFromZip()
    {
        using var tempRoot = new TemporaryDirectory();
        var appId = Guid.NewGuid();
        var packagePath = Path.Combine(tempRoot.Path, "app.zip");

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            var indexEntry = archive.CreateEntry("runtime/frontend/index.html");
            await using (var writer = new StreamWriter(indexEntry.Open()))
            {
                await writer.WriteAsync("<!doctype html><html></html>");
            }

            var assetEntry = archive.CreateEntry("runtime/frontend/assets/app.css");
            await using (var writer = new StreamWriter(assetEntry.Open()))
            {
                await writer.WriteAsync("body{}");
            }
        }

        using var stream = File.OpenRead(packagePath);
        IFormFile formFile = new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(packagePath));
        var service = CreateService(tempRoot.Path);

        await service.ExportFromDeployPackageAsync(appId, formFile, CancellationToken.None);

        var targetPath = Path.Combine(tempRoot.Path, "static-runtime", "apps", appId.ToString());
        Assert.That(File.Exists(Path.Combine(targetPath, "index.html")), Is.True);
        Assert.That(File.Exists(Path.Combine(targetPath, "assets", "app.css")), Is.True);
    }

    private static StaticRuntimeExportService CreateService(string contentRootPath)
    {
        return new StaticRuntimeExportService(
            new StubWebHostEnvironment(contentRootPath),
            Options.Create(new WebAppConfig
            {
                StaticRuntimeRoot = "static-runtime"
            }));
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public StubWebHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string ApplicationName { get; set; } = "Automaton.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "automaton-static-runtime-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
