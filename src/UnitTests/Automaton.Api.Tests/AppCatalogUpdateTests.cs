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
    public void SourceFingerprint_ChangesWhenAFileIsEdited_AddedOrRemoved_SoACachedAnswerIsNotReused()
    {
        var directory = Path.Combine(Path.GetTempPath(), "e2e-1149-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "index.html");
            File.WriteAllText(file, "hi");
            var untouched = AppCatalogUpdateService.SourceFingerprint(directory);
            Assert.That(AppCatalogUpdateService.SourceFingerprint(directory), Is.EqualTo(untouched));

            File.WriteAllText(file, "hi");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
            var rewritten = AppCatalogUpdateService.SourceFingerprint(directory);
            File.WriteAllText(Path.Combine(directory, "extra.txt"), "x");
            var added = AppCatalogUpdateService.SourceFingerprint(directory);
            File.Delete(Path.Combine(directory, "extra.txt"));
            File.Delete(file);

            Assert.Multiple(() =>
            {
                Assert.That(rewritten, Is.Not.EqualTo(untouched), "same size, new time");
                Assert.That(added, Is.Not.EqualTo(rewritten));
                Assert.That(AppCatalogUpdateService.SourceFingerprint(directory), Is.Not.EqualTo(added));
                Assert.That(AppCatalogUpdateService.SourceFingerprint(directory + "-missing"), Is.EqualTo("none"));
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

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
    public void IsEdited_IgnoresFieldsTheServerWrites_ButNotRealEdits()
    {
        var original = new[] { ("automaton.json", "{\"name\":\"Tool\",\"application\":{\"kind\":\"web\"}}"), ("index.html", "hi") };
        var published = ("automaton.json", "{\"appId\":\"x\",\"name\":\"Mine\",\"slug\":\"mine\",\"application\":{\"kind\":\"web\"}}");
        var codeEdit = ("automaton.json", "{\"name\":\"Tool\",\"application\":{\"kind\":\"static\"}}");
        Assert.Multiple(() =>
        {
            Assert.That(AppCatalogUpdateService.IsEdited(Archive(original), Source, Folder(published, ("index.html", "hi"), (".automatonignore", "bin/"))), Is.False);
            Assert.That(AppCatalogUpdateService.IsEdited(Archive(original), Source, Folder(codeEdit, ("index.html", "hi"))), Is.True);
            Assert.That(AppCatalogUpdateService.IsEdited(Archive(original), Source, Folder(published, ("index.html", "changed"))), Is.True);
        });
    }

    [Test]
    public void KeepServerWrittenFiles_CarriesNameSlugAndIgnoreFileIntoTheNewVersion()
    {
        var old = Folder(("automaton.json", "{\"appId\":\"x\",\"name\":\"Mine\",\"slug\":\"mine\",\"application\":{\"kind\":\"web\"}}"), (".automatonignore", "bin/"));
        var staging = Folder(("automaton.json", "{\"name\":\"Tool\",\"application\":{\"kind\":\"static\"}}"));
        AppCatalogUpdateService.KeepServerWrittenFiles(old, staging);
        var merged = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(staging, "automaton.json")))!;
        Assert.Multiple(() =>
        {
            Assert.That((string?)merged["slug"], Is.EqualTo("mine"));
            Assert.That((string?)merged["name"], Is.EqualTo("Mine"));
            Assert.That((string?)merged["appId"], Is.EqualTo("x"));
            Assert.That((string?)merged["application"]!["kind"], Is.EqualTo("static"));
            Assert.That(File.ReadAllText(Path.Combine(staging, ".automatonignore")), Is.EqualTo("bin/"));
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

    [Test]
    public void Backfill_ReadsTheNameFromTheAppsOwnManifest_AndIgnoresBadOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AppCatalogBackfillService.ManifestName(Folder(("automaton.json", "{\"name\":\"Off-site Backup\"}"))), Is.EqualTo("Off-site Backup"));
            Assert.That(AppCatalogBackfillService.ManifestName(Folder(("automaton.json", "not json"))), Is.Null);
            Assert.That(AppCatalogBackfillService.ManifestName(Folder(("automaton.json", "[1]"))), Is.Null);
            Assert.That(AppCatalogBackfillService.ManifestName(Folder(("automaton.json", "{\"name\":5}"))), Is.Null);
            Assert.That(AppCatalogBackfillService.ManifestName(Folder(("index.html", "x"))), Is.Null);
        });
    }
}
