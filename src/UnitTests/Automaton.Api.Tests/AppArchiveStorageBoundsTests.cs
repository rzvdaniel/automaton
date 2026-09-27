using System.IO.Compression;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public sealed class AppArchiveStorageBoundsTests
{
    [Test]
    public void Validate_AllowsContentWithinExpandedAndEntryBounds()
    {
        using var archive = CreateArchive(("index.html", "hello"), ("assets/site.css", "style"));

        Assert.DoesNotThrow(() => AppArchiveStorageBounds.Validate(archive, maximumExpandedBytes: 10, maximumEntries: 2));
    }

    [Test]
    public void Validate_RejectsExpandedContentBeforeExtraction()
    {
        using var archive = CreateArchive(("index.html", "hello"), ("assets/site.css", "style"));

        Assert.Throws<InvalidDataException>(() =>
            AppArchiveStorageBounds.Validate(archive, maximumExpandedBytes: 9, maximumEntries: 2));
    }

    [Test]
    public void Validate_RejectsExcessiveEntryCount()
    {
        using var archive = CreateArchive(("one.txt", "1"), ("two.txt", "2"));

        Assert.Throws<InvalidDataException>(() =>
            AppArchiveStorageBounds.Validate(archive, maximumExpandedBytes: 10, maximumEntries: 1));
    }

    [Test]
    public async Task ValidateDirectory_RejectsExpandedTreeAndPreservesTheDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-storage-bounds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "content");

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                AppArchiveStorageBounds.ValidateDirectory(root, maximumBytes: 3, maximumEntries: 1));
            Assert.That(File.ReadAllText(Path.Combine(root, "index.html")), Is.EqualTo("content"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ValidateDirectory_RejectsExcessiveFileCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-storage-bounds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "one.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(root, "two.txt"), "2");

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                AppArchiveStorageBounds.ValidateDirectory(root, maximumBytes: 10, maximumEntries: 1));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ValidateFileWrite_RejectsCumulativeGrowthBeforeChangingSource()
    {
        var root = CreateTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "12345678");

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                AppArchiveStorageBounds.ValidateFileWrite(root, "app.js", replacementBytes: 3, maximumBytes: 10));
            Assert.That(File.Exists(Path.Combine(root, "app.js")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(root, "index.html")), Is.EqualTo("12345678"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ValidateFileWrite_AllowsReplacementWithoutDoubleCountingOldContents()
    {
        var root = CreateTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "assets"), "old!!");

        try
        {
            Assert.DoesNotThrow(() =>
                AppArchiveStorageBounds.ValidateFileWrite(root, "assets", replacementBytes: 10, maximumBytes: 10));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CopyExactlyAsync_RejectsBytesPastDeclaredLengthBeforeWritingThem()
    {
        await using var input = new MemoryStream("excess"u8.ToArray());
        await using var output = new MemoryStream();

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AppArchiveStorageBounds.CopyExactlyAsync(input, output, expectedBytes: 3, maximumBytes: 10));
        Assert.That(output.Length, Is.Zero);
    }

    [Test]
    public async Task CopyExactlyAsync_RejectsADeclaredLengthAboveTheConfiguredLimitBeforeWriting()
    {
        await using var input = new MemoryStream("data"u8.ToArray());
        await using var output = new MemoryStream();

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AppArchiveStorageBounds.CopyExactlyAsync(input, output, expectedBytes: 11, maximumBytes: 10));
        Assert.That(output.Length, Is.Zero);
    }

    private static ZipArchive CreateArchive(params (string Path, string Content)[] files)
    {
        var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(file.Path).Open());
                writer.Write(file.Content);
            }
        }

        bytes.Position = 0;
        return new ZipArchive(bytes, ZipArchiveMode.Read);
    }

    private static string CreateTestDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-storage-bounds-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
