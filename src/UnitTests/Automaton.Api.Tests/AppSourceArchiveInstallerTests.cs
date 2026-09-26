using System.IO.Compression;
using Automaton.Api.Services;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class AppSourceArchiveInstallerTests
{
    [Test]
    public async Task InstallAsync_ReplacesExistingSourceAndRemovesTemporaryDirectories()
    {
        var root = CreateTestRoot();
        var sourcePath = Path.Combine(root, "source");
        Directory.CreateDirectory(sourcePath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "old.txt"), "old");

        try
        {
            await using var package = CreateArchive(("source/new.txt", "new"));
            await AppSourceArchiveInstaller.InstallAsync(package, sourcePath, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(sourcePath, "old.txt")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(sourcePath, "new.txt")), Is.EqualTo("new"));
                Assert.That(Directory.GetDirectories(root), Is.EqualTo(new[] { sourcePath }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task InstallAsync_RejectsUnsafeEntryAndPreservesExistingSource()
    {
        var root = CreateTestRoot();
        var sourcePath = Path.Combine(root, "source");
        Directory.CreateDirectory(sourcePath);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "old.txt"), "preserve");

        try
        {
            await using var package = CreateArchive(
                ("source/new.txt", "new"),
                ("source/../../outside.txt", "unsafe"));

            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await AppSourceArchiveInstaller.InstallAsync(package, sourcePath, CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(Path.Combine(sourcePath, "old.txt")), Is.EqualTo("preserve"));
                Assert.That(File.Exists(Path.Combine(root, "outside.txt")), Is.False);
                Assert.That(Directory.GetDirectories(root), Is.EqualTo(new[] { sourcePath }));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTestRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-source-install-tests", Guid.NewGuid().ToString("N"));
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
