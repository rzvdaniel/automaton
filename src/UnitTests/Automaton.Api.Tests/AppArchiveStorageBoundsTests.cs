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
    public async Task CopyEntryAsync_RejectsBytesPastDeclaredEntryLengthBeforeWritingThem()
    {
        await using var input = new MemoryStream("excess"u8.ToArray());
        await using var output = new MemoryStream();

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await AppArchiveStorageBounds.CopyEntryAsync(input, output, expectedBytes: 3));
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
}
