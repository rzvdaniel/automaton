using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class CatalogCommandTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";
    private string directory = null!;

    [SetUp]
    public void CreateDirectory() => directory = Path.Combine(Path.GetTempPath(), "automaton-catalog-tests-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void DeleteDirectory() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }

    [Test]
    public void CatalogCommand_HasInitAndBuild() =>
        Assert.That(CatalogCommand.Build(new ConfigService()).Subcommands.Select(c => c.Name), Is.EquivalentTo(new[] { "init", "build" }));

    [Test]
    public void Init_WritesTheSkeletonAndNothingElse()
    {
        Directory.CreateDirectory(directory);

        var written = CatalogInitService.Write(new ConfigService(), directory);

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EquivalentTo(new[]
            {
                ".github/workflows/catalog.yml", "README.md", "catalog-apps.json",
                "apps/hello/automaton.json", "apps/hello/.automatonignore", "apps/hello/index.html", "apps/hello/health.txt"
            }));
            Assert.That(Directory.Exists(Path.Combine(directory, ".git")), Is.False, "init never runs git");
            var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "apps/hello/automaton.json"))).RootElement;
            Assert.That(manifest.GetProperty("name").GetString(), Is.EqualTo("Hello"));
            Assert.That(manifest.GetProperty("summary").GetString(), Is.Not.Empty);
            Assert.That(manifest.GetProperty("application").GetProperty("health").GetProperty("path").GetString(), Is.EqualTo("/health.txt"));
            var workflow = File.ReadAllText(Path.Combine(directory, ".github/workflows/catalog.yml"));
            Assert.That(workflow, Does.Contain("automaton catalog build").And.Contain("branches: [main]")
                .And.Contain("pull-requests: write").And.Contain("gh pr merge").And.Contain("catalog-bot").And.Not.Contain("\\\\"));
        });
    }

    [Test]
    public void Init_RefusesToOverwriteExistingFilesAndWritesNothing()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "README.md"), "mine");

        var failure = Assert.Throws<IOException>(() => CatalogInitService.Write(new ConfigService(), directory));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("README.md"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "README.md")), Is.EqualTo("mine"));
            Assert.That(Directory.Exists(Path.Combine(directory, "apps")), Is.False);
        });
    }

    [Test]
    public void Build_OfTheInitSkeleton_ListsTheExampleAppPinnedToItsCommitWithTheFieldsAServerReads()
    {
        Directory.CreateDirectory(directory);
        CatalogInitService.Write(new ConfigService(), directory);

        var (json, problems) = CatalogBuildService.Build(directory, "acme/apps", _ => Sha);

        Assert.That(problems, Is.Empty);
        var catalog = JsonDocument.Parse(json!).RootElement;
        var app = catalog.GetProperty("apps")[0];
        Assert.Multiple(() =>
        {
            Assert.That(catalog.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(catalog.GetProperty("repo").GetString(), Is.EqualTo("acme/apps"));
            Assert.That(app.GetProperty("id").GetString(), Is.EqualTo("hello"));
            Assert.That(app.GetProperty("folder").GetString(), Is.EqualTo("apps/hello"));
            Assert.That(app.GetProperty("commit").GetString(), Is.EqualTo(Sha));
            Assert.That(app.GetProperty("minServerVersion").GetString(), Is.EqualTo("0.2.32"));
            Assert.That(app.GetProperty("category").GetString(), Is.EqualTo("Examples"));
            Assert.That(app.GetProperty("permissions").GetArrayLength(), Is.EqualTo(0));
        });
    }

    [Test]
    public void Build_TakesPermissionsFromTheAppManifest()
    {
        Directory.CreateDirectory(Path.Combine(directory, "a/tool"));
        File.WriteAllText(Path.Combine(directory, "catalog-apps.json"), """{"apps":[{"folder":"a/tool","category":"Ops","minServerVersion":"1.2.3"}]}""");
        File.WriteAllText(Path.Combine(directory, "a/tool/automaton.json"), """{"name":"Tool","summary":"s","application":{"permissions":["backups:read"]}}""");

        var (json, _) = CatalogBuildService.Build(directory, "acme/apps", _ => Sha);

        Assert.That(JsonDocument.Parse(json!).RootElement.GetProperty("apps")[0].GetProperty("permissions")[0].GetString(), Is.EqualTo("backups:read"));
    }

    [TestCase("""{"apps":[{"folder":"a/tool","category":"Ops","minServerVersion":"1.2"}]}""", "minServerVersion")]
    [TestCase("""{"apps":[{"folder":"a/tool","category":"","minServerVersion":"1.2.3"}]}""", "category is empty")]
    [TestCase("""{"apps":[{"folder":"a/../tool","category":"Ops","minServerVersion":"1.2.3"}]}""", "folder name is not allowed")]
    [TestCase("""{"apps":[{"folder":"missing","category":"Ops","minServerVersion":"1.2.3"}]}""", "no automaton.json")]
    [TestCase("""{"apps":[{"folder":"a/tool","category":"Ops","minServerVersion":"1.2.3"},{"folder":"b/tool","category":"Ops","minServerVersion":"1.2.3"}]}""", "same folder name")]
    public void Build_ReportsInvalidEntries(string list, string expected)
    {
        foreach (var folder in new[] { "a/tool", "b/tool" })
        {
            Directory.CreateDirectory(Path.Combine(directory, folder));
            File.WriteAllText(Path.Combine(directory, folder, "automaton.json"), """{"name":"Tool","summary":"s"}""");
        }
        File.WriteAllText(Path.Combine(directory, "catalog-apps.json"), list);

        var (json, problems) = CatalogBuildService.Build(directory, "acme/apps", _ => Sha);

        Assert.Multiple(() =>
        {
            Assert.That(json, Is.Null);
            Assert.That(string.Join(" | ", problems), Does.Contain(expected));
        });
    }

    [Test]
    public void Build_AFolderThatIsNotCommitted_HasNoPinnedCommitAndIsReported()
    {
        Directory.CreateDirectory(Path.Combine(directory, "a/tool"));
        File.WriteAllText(Path.Combine(directory, "catalog-apps.json"), """{"apps":[{"folder":"a/tool","category":"Ops","minServerVersion":"1.2.3"}]}""");
        File.WriteAllText(Path.Combine(directory, "a/tool/automaton.json"), """{"name":"Tool","summary":"s"}""");

        var (json, problems) = CatalogBuildService.Build(directory, "acme/apps", _ => "");

        Assert.That(json, Is.Null);
        Assert.That(string.Join(" ", problems), Does.Contain("no pinned commit"));
    }

    [TestCase("[]", "catalog-apps.json", "must be a JSON object")]
    [TestCase("""{"name":"T","summary":"s","application":"x"}""", "a/tool/automaton.json", "no pinned commit|permissions")]
    [TestCase("[]", "a/tool/automaton.json", "must be a JSON object")]
    public void Build_ReportsMalformedJsonShapesInsteadOfCrashing(string contents, string file, string expected)
    {
        Directory.CreateDirectory(Path.Combine(directory, "a/tool"));
        File.WriteAllText(Path.Combine(directory, "catalog-apps.json"), """{"apps":[{"folder":"a/tool","category":"Ops","minServerVersion":"1.2.3"}]}""");
        File.WriteAllText(Path.Combine(directory, "a/tool/automaton.json"), """{"name":"Tool","summary":"s"}""");
        File.WriteAllText(Path.Combine(directory, file), contents);

        var (json, problems) = CatalogBuildService.Build(directory, "acme/apps", _ => file == "a/tool/automaton.json" && contents.Contains("application") ? Sha : "");

        if (contents.Contains("application")) Assert.That(json, Is.Not.Null, "a non-object application just has no permissions");
        else Assert.That(string.Join(" ", problems), Does.Match(expected));
    }

    [Test]
    public void Build_NeedsTheCatalogListAndARepository()
    {
        Directory.CreateDirectory(directory);
        Assert.That(CatalogBuildService.Build(directory, "acme/apps").Problems.Single(), Does.Contain("catalog-apps.json was not found"));

        File.WriteAllText(Path.Combine(directory, "catalog-apps.json"), """{"apps":[]}""");
        var previous = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", null);
        try { Assert.That(CatalogBuildService.Build(directory, null).Problems.Single(), Does.Contain("repository is unknown")); }
        finally { Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", previous); }
    }
}
