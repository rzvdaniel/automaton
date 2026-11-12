using System.IO.Compression;
using Automaton.Api.Models;
using Automaton.Api.Services;
using NUnit.Framework;

namespace Automaton.Api.Tests;

[TestFixture]
public class AppCatalogUpdateTests
{
    private static ZipArchive Archive(params (string Path, string Text)[] files)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (path, text) in files)
                using (var writer = new StreamWriter(zip.CreateEntry("repo-abc/tool/" + path).Open())) writer.Write(text);
        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private static string Folder(params (string Path, string Text)[] files)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        foreach (var (path, text) in files) File.WriteAllText(Path.Combine(dir, path), text);
        return dir;
    }

    private static readonly GitAppSource Source = new("owner/repo", "tool", new string('a', 40));

    [Test]
    public void IsEdited_IsFalseForUntouchedFiles_AndTrueForChangedAddedOrMissingFiles()
    {
        var original = new[] { ("automaton.json", "{}"), ("index.html", "hi") };
        Assert.Multiple(() =>
        {
            Assert.That(AppCatalogUpdateService.IsEdited(Archive(original), Source, Folder(original)), Is.False);
            Assert.That(AppCatalogUpdateService.IsEdited(Archive(original), Source, Folder(("automaton.json", "{}"), ("index.html", "changed"))), Is.True);
            Assert.That(AppCatalogUpdateService.IsEdited(Archive(original), Source, Folder(("automaton.json", "{}"), ("index.html", "hi"), ("extra.txt", "x"))), Is.True);
            Assert.That(AppCatalogUpdateService.IsEdited(Archive(original), Source, Folder(("automaton.json", "{}"))), Is.True);
        });
    }

    [Test]
    public void Added_ListsOnlyNamesTheNewVersionAdds() =>
        Assert.That(AppCatalogUpdateService.Added(["A", "B"], ["B", "C"]), Is.EqualTo(new[] { "C" }));

    [Test]
    public void Backfill_OnlyTouchesAppsWithNoRecordedSource()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AppCatalogBackfillService.IsCandidate(null, null, null), Is.True);
            Assert.That(AppCatalogBackfillService.IsCandidate("", "", ""), Is.True);
            Assert.That(AppCatalogBackfillService.IsCandidate("owner/repo", "tool", new string('a', 40)), Is.False);
            Assert.That(AppCatalogBackfillService.IsCandidate("owner/repo", null, null), Is.False);
        });
    }

    [Test]
    public void Backfill_NeedsTheCatalogFilesOnDisk_NotJustTheName()
    {
        var catalog = new[] { ("automaton.json", "{}"), ("index.html", "hi"), ("run.js", "x"), ("README.md", "r") };
        Assert.Multiple(() =>
        {
            Assert.That(AppCatalogBackfillService.SharesFiles(Archive(catalog), Source, Folder(catalog)), Is.True);
            Assert.That(AppCatalogBackfillService.SharesFiles(Archive(catalog), Source, Folder(("automaton.json", "{\"x\":1}"), ("index.html", "edited"))), Is.True);
            Assert.That(AppCatalogBackfillService.SharesFiles(Archive(catalog), Source, Folder(("index.html", "hi"), ("run.js", "x"), ("README.md", "r"))), Is.False);
            Assert.That(AppCatalogBackfillService.SharesFiles(Archive(catalog), Source, Folder(("automaton.json", "{}"), ("notes.txt", "mine"))), Is.False);
        });
    }

    [Test]
    public void Backfill_MatchesAnOlderOrRenamedCatalogName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AppCatalogBackfillService.NameMatches("Automaton Off-site Backup", "Off-site Backup"), Is.True);
            Assert.That(AppCatalogBackfillService.NameMatches("off-site backup", "Off-site Backup"), Is.True);
            Assert.That(AppCatalogBackfillService.NameMatches("My notes", "Off-site Backup"), Is.False);
            Assert.That(AppCatalogBackfillService.NameMatches("", "Off-site Backup"), Is.False);
        });
    }
}
