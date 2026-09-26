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
