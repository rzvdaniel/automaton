using System.IO.Compression;
using Automaton.Api.Services;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppRuntimeArchiveInstallerTests
{
    [Test]
    public async Task InstallAsync_RejectsAccumulatedFilesAboveBoundWithoutChangingRuntime()
    {
        var root = CreateTestRoot();
        var runtimePath = Path.Combine(root, "live");
        Directory.CreateDirectory(runtimePath);
        await File.WriteAllTextAsync(Path.Combine(runtimePath, "index.html"), "existing");

        try
        {
            await using var package = CreateArchive(("assets/new.js", "newfile!"));

            var exception = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await AppRuntimeArchiveInstaller.InstallAsync(
                    package, runtimePath, clean: false, cancellationToken: CancellationToken.None, maximumBytes: 10));

            Assert.That(exception!.Message, Does.Contain("storage limit"));
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(runtimePath, "index.html")), Is.EqualTo("existing"));
                Assert.That(File.Exists(Path.Combine(runtimePath, "assets", "new.js")), Is.False);
                Assert.That(Directory.GetDirectories(root), Is.EqualTo(new[] { runtimePath }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task InstallAsync_CleanUploadReplacesRuntimeAndRemovesStaging()
    {
        var root = CreateTestRoot();
        var runtimePath = Path.Combine(root, "live");
        Directory.CreateDirectory(runtimePath);
        await File.WriteAllTextAsync(Path.Combine(runtimePath, "old.html"), "old");

        try
        {
            await using var package = CreateArchive(("index.html", "new"));

            var result = await AppRuntimeArchiveInstaller.InstallAsync(
                package, runtimePath, clean: true, cancellationToken: CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Errors, Is.Empty);
                Assert.That(File.Exists(Path.Combine(runtimePath, "old.html")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(runtimePath, "index.html")), Is.EqualTo("new"));
                Assert.That(Directory.GetDirectories(root), Is.EqualTo(new[] { runtimePath }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task InstallAsync_MergeReplacesExistingNestedFileWithinCombinedLimit()
    {
        var root = CreateTestRoot();
        var runtimePath = Path.Combine(root, "live");
        Directory.CreateDirectory(Path.Combine(runtimePath, "assets"));
        await File.WriteAllTextAsync(Path.Combine(runtimePath, "assets", "site.css"), "old!!");

        try
        {
            await using var package = CreateArchive(("assets/site.css", "new!!"));

            var result = await AppRuntimeArchiveInstaller.InstallAsync(
                package, runtimePath, clean: false, cancellationToken: CancellationToken.None, maximumBytes: 5);

            Assert.Multiple(() =>
            {
                Assert.That(result.Errors, Is.Empty);
                Assert.That(File.ReadAllText(Path.Combine(runtimePath, "assets", "site.css")), Is.EqualTo("new!!"));
                Assert.That(Directory.GetDirectories(root), Is.EqualTo(new[] { runtimePath }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task InstallAsync_WriteFailureLeavesPreviousRuntimeAndRemovesStaging()
    {
        var root = CreateTestRoot();
        var runtimePath = Path.Combine(root, "live");
        Directory.CreateDirectory(Path.Combine(runtimePath, "index.html"));
        await File.WriteAllTextAsync(Path.Combine(runtimePath, "index.html", "existing.js"), "existing");

        try
        {
            await using var package = CreateArchive(("index.html", "cannot-replace-directory"));

            var result = await AppRuntimeArchiveInstaller.InstallAsync(
                package, runtimePath, clean: false, cancellationToken: CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.Errors, Is.Not.Empty);
                Assert.That(File.ReadAllText(Path.Combine(runtimePath, "index.html", "existing.js")), Is.EqualTo("existing"));
                Assert.That(Directory.GetDirectories(root), Is.EqualTo(new[] { runtimePath }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-runtime-install-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static MemoryStream CreateArchive(params (string Path, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(file.Path).Open());
                writer.Write(file.Content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
