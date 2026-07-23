using Automaton.Cli.Commands;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Commands;

[TestFixture]
public class PullCommandTests
{
    [Test]
    public void SelectSourceFiles_PreservesCompleteAiDefinedLayout()
    {
        var files = PullCommand.SelectSourceFiles(
        [
            "automaton.json",
            "frontend/src/main.ts",
            "server/output/app.dll",
            "packages/shared/schema.json",
            "schemas/app.graphql",
            @"server\output\app.runtimeconfig.json"
        ]);

        Assert.That(files, Does.Contain("automaton.json"));
        Assert.That(files, Does.Contain("server/output/app.dll"));
        Assert.That(files, Does.Contain("packages/shared/schema.json"));
        Assert.That(files, Does.Contain("schemas/app.graphql"));
        Assert.That(files, Does.Contain("server/output/app.runtimeconfig.json"));
    }

    [TestCase("../outside.txt")]
    [TestCase(@"..\outside.txt")]
    [TestCase("/absolute.txt")]
    public void ResolveTargetPath_WhenRemotePathEscapesTarget_Throws(string relativePath)
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => PullCommand.ResolveTargetPath(targetDirectory, relativePath));

        Assert.That(exception!.Message, Does.Contain("source path"));
    }

    [Test]
    public void ResolveTargetPath_WhenRemotePathIsNested_ReturnsPathInsideTarget()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));

        var resolved = PullCommand.ResolveTargetPath(targetDirectory, @"packages\shared\schema.json");

        Assert.That(
            resolved,
            Is.EqualTo(Path.Combine(targetDirectory, "packages", "shared", "schema.json")));
    }
}
