using System.Text.Json;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class PublishServiceTests
{
    private static DeploymentTargetInfo Target(bool isDefault, string? error)
        => new("t", "n", "s", isDefault, "Failed", null, null, error);

    [Test]
    public void PickDeployError_PrefersTheDefaultTarget()
        => Assert.That(PublishService.PickDeployError([Target(false, "staging broke"), Target(true, "live broke")]),
            Is.EqualTo("live broke"));

    [Test]
    public void PickDeployError_FallsBackToAnotherTargetWhenDefaultHasNone()
        => Assert.That(PublishService.PickDeployError([Target(true, null), Target(false, "staging broke")]),
            Is.EqualTo("staging broke"));

    [Test]
    public async Task DescribeEmptyApp_FallsBackWhenTheLookupFails()
    {
        var (message, hints) = await PublishService.DescribeEmptyAppAsync("nothing to publish",
            () => throw new HttpRequestException("boom"));
        Assert.That(message, Is.EqualTo("nothing to publish"));
        Assert.That(hints, Is.EqualTo(new[] { PublishService.EmptyAppHint }));
    }

    [Test]
    public async Task DescribeEmptyApp_ShowsBothMessagesAndHintsWhenADeployFailed()
    {
        var (message, hints) = await PublishService.DescribeEmptyAppAsync("nothing to publish",
            () => Task.FromResult<string?>("build failed"));
        Assert.That(message, Does.Contain("nothing to publish").And.Contain("build failed"));
        Assert.That(hints, Has.Length.EqualTo(2));
        Assert.That(hints[0], Is.EqualTo(PublishService.EmptyAppHint));
    }

    [Test]
    public async Task DescribeEmptyApp_NotDeployedNamesTheFailedDeployWithoutTheDeclareHint()
    {
        var (message, hints) = await PublishService.DescribeEmptyAppAsync(PublishService.NotDeployedMessage,
            () => Task.FromResult<string?>("build failed"));
        Assert.That(message, Does.Contain("no successful deploy yet").And.Contain("the last deploy failed: build failed"));
        Assert.That(hints, Has.Length.EqualTo(1));
        Assert.That(hints, Does.Not.Contain(PublishService.EmptyAppHint));
    }

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
    public void FindEmptyAppProblem_SaysNotDeployedWhenOnlyTheLocalManifestDeclaresARuntime()
    {
        var dir = Directory.CreateTempSubdirectory("e2e-publish-local").FullName;
        try
        {
            using var app = JsonDocument.Parse("""{ "application": { "runtime": { "command": null } } }""");
            File.WriteAllText(Path.Combine(dir, "automaton.json"),
                """{ "application": { "runtime": { "command": { "executable": "node" } } } }""");

            var problem = PublishService.FindEmptyAppProblem(app.RootElement, dir);
            Assert.That(problem, Is.EqualTo(PublishService.NotDeployedMessage));
            Assert.That(problem, Does.Not.Contain("declares no runtime command"));
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
