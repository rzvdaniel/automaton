using System.Text.Json;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class PublishServiceTests
{
    [Test]
    public void ReadCurrentListing_KeepsTheAppsCategoryAndTags()
    {
        using var app = JsonDocument.Parse("""{ "category": "Games", "tags": "game,words" }""");

        var listing = PublishService.ReadCurrentListing(app.RootElement);

        Assert.Multiple(() =>
        {
            Assert.That(listing.Category, Is.EqualTo("Games"));
            Assert.That(listing.Tags, Is.EqualTo("game,words"));
        });
    }

    [Test]
    public void ReadCurrentListing_LeavesAnUnlistedAppEmpty()
    {
        using var app = JsonDocument.Parse("""{ "category": null }""");

        Assert.That(PublishService.ReadCurrentListing(app.RootElement), Is.EqualTo(((string?)null, (string?)null)));
    }

    [Test]
    public void FindEmptyAppProblem_RefusesAnAppWithNothingToRun()
    {
        var dir = Directory.CreateTempSubdirectory("e2e-publish-empty").FullName;
        try
        {
            using var app = JsonDocument.Parse("""{ "application": { "runtime": { "command": null }, "isStaticSite": false } }""");

            Assert.That(PublishService.FindEmptyAppProblem(app.RootElement, dir), Does.Contain("no runtime command, static site or Action"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void FindEmptyAppProblem_AllowsRuntimeStaticSiteAndActions()
    {
        var dir = Directory.CreateTempSubdirectory("e2e-publish-ok").FullName;
        try
        {
            using var runtime = JsonDocument.Parse("""{ "application": { "runtime": { "command": { "executable": "node" } } } }""");
            using var site = JsonDocument.Parse("""{ "application": { "isStaticSite": true } }""");
            Assert.That(PublishService.FindEmptyAppProblem(runtime.RootElement, dir), Is.Null);
            Assert.That(PublishService.FindEmptyAppProblem(site.RootElement, dir), Is.Null);

            var action = Directory.CreateDirectory(Path.Combine(dir, "actions", "run"));
            File.WriteAllText(Path.Combine(action.FullName, "action.yaml"),
                "apiVersion: automaton.dev/v1\nkind: Action\nmetadata:\n  key: run\nspec:\n  runtime: bash\n  entrypoint: run.sh\n");
            File.WriteAllText(Path.Combine(action.FullName, "run.sh"), "echo hi");
            using var empty = JsonDocument.Parse("""{ "application": {} }""");
            Assert.That(PublishService.FindEmptyAppProblem(empty.RootElement, dir), Is.Null);
        }
        finally { Directory.Delete(dir, true); }
    }
}
