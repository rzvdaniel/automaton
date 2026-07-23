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

    [Test]
    public void SelectSourceFiles_PreservesCaseDistinctRemotePaths()
    {
        var files = PullCommand.SelectSourceFiles(["Models/User.cs", "models/User.cs"]);

        Assert.That(files, Is.EqualTo(new[] { "Models/User.cs", "models/User.cs" }));
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

    [Test]
    public void PrepareDownloads_WhenAnyPathIsInvalid_ThrowsBeforeReturningAnyDownloads()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));

        Assert.Throws<InvalidOperationException>(
            () => PullCommand.PrepareDownloads(["frontend/src/main.ts", "../outside.txt"], targetDirectory));
    }

    [Test]
    [Platform(Exclude = "Win")]
    public void ResolveTargetPath_WhenCaseVariantSiblingEscapesTarget_Throws()
    {
        var parentDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(parentDirectory, "MyApp");

        Assert.Throws<InvalidOperationException>(
            () => PullCommand.ResolveTargetPath(targetDirectory, "../myapp/outside.txt"));
    }

    [Test]
    public void PrepareDownloads_WhenPathsCollideOnCaseInsensitiveFilesystem_Throws()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Throws<InvalidOperationException>(
                () => PullCommand.PrepareDownloads(["Models/User.cs", "models/User.cs"], targetDirectory));
            return;
        }

        var downloads = PullCommand.PrepareDownloads(["Models/User.cs", "models/User.cs"], targetDirectory);
        Assert.That(downloads, Has.Count.EqualTo(2));
    }

    [Test]
    public void PrepareDownloads_WhenDestinationParentIsReparsePoint_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "automaton-pull-tests", Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "project");
        Directory.CreateDirectory(targetDirectory);
        var linkedDirectory = Path.Combine(targetDirectory, "shared");
        var outsideDirectory = Path.Combine(root, "outside");
        Directory.CreateDirectory(outsideDirectory);

        try
        {
            Directory.CreateSymbolicLink(linkedDirectory, outsideDirectory);
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Ignore("The test environment does not allow symbolic links.");
            return;
        }
        catch (IOException)
        {
            Assert.Ignore("The test environment does not allow symbolic links.");
            return;
        }

        Assert.Throws<InvalidOperationException>(
            () => PullCommand.PrepareDownloads(["shared/file.txt"], targetDirectory));
    }
}
