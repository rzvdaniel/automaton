using System.IO.Compression;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerPackageFileModeTests
{
    [Test]
    public void ExtractedFilesAreReadableOnlyByOwnerAndRunnerGroupWhateverModeTheAuthorUsed()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("Unix file modes only.");
        var root = Path.Combine(Path.GetTempPath(), "pkg-mode-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var package = Path.Combine(root, "app.zip");
            using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                Add(zip, "index.html", 0b110_000_000);          // 0600, author's umask 077
                Add(zip, "bin/start.sh", 0b111_000_000);        // 0700, keeps its executable bit
                Add(zip, "private/data.json", 0b100_000_000);   // 0400
                Add(zip, "world/open.txt", 0b111_111_111);      // 0777 must not stay world-accessible
            }
            var staging = Path.Combine(root, "staging");
            Directory.CreateDirectory(staging);

            RunnerDeploymentWorkspace.ExtractPackageSafely(package, staging);

            Mode(staging, "index.html", Is.EqualTo((UnixFileMode)0b110_100_000));
            Mode(staging, "private/data.json", Is.EqualTo((UnixFileMode)0b110_100_000));
            Mode(staging, "bin/start.sh", Is.EqualTo((UnixFileMode)0b111_101_000));
            Mode(staging, "world/open.txt", Is.EqualTo((UnixFileMode)0b111_101_000));
            foreach (var directory in new[] { "bin", "private", "world" })
                Mode(staging, directory, Is.EqualTo((UnixFileMode)0b111_111_000));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void ReadinessFailureNamesUnreadableFilesAsTheLikelyCause()
    {
        var policy = new RunnerRuntimeNetworkPolicy(new RunnerRuntimeRegistry());
        var health = new ApplicationHealthSpec { Path = "/", StartupTimeoutSeconds = 1 };
        var failure = Assert.ThrowsAsync<TimeoutException>(() => policy.WaitForReadinessAsync(1, health, CancellationToken.None));
        Assert.That(failure!.Message, Does.Contain("did not become ready"));
        Assert.That(failure.Message, Does.Contain("runtime user probably cannot read"));
    }

    [Test]
    public async Task RuntimeFilesAreChownedToTheContractUserWithOnlyChownCapability()
    {
        var calls = new List<List<string>>();
        var runtime = new DockerContainerRuntime((args, _, _, _) =>
        {
            calls.Add(args.ToList());
            if (args.Contains("image") && args.Contains("--format") && args.Contains("{{index .RepoDigests 0}}"))
                return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "runtime:1@sha256:" + new string('a', 64), ""));
            return Task.FromResult(new DockerContainerRuntime.CommandResult(0, "container-1", ""));
        });
        await runtime.CreateApplicationAsync(new DockerApplicationContainerRequest(
            "app", "runtime:1", Path.GetTempPath(), ".", new ApplicationCommandSpec { Executable = "server" },
            8080, 18080, Path.GetTempPath(), new Dictionary<string, string>(), Guid.NewGuid(), Guid.NewGuid(), User: "33:33"));

        var chown = calls.Single(call => call.Contains("chown"));
        Assert.That(chown.Any(a => a.StartsWith("runtime:1")), Is.False, "the helper must not run the app image");
        Assert.That(chown, Does.Contain("CHOWN"));
        Assert.That(chown, Does.Not.Contain("DAC_OVERRIDE"));
        Assert.That(chown, Does.Contain("--network").And.Contain("none"));
        Assert.That(chown.Skip(chown.IndexOf("chown") + 2).ToArray(), Is.EqualTo(new[] { "-R", "-h", "33", "/automaton/app" }));
    }

    private static void Mode(string root, string relative, NUnit.Framework.Constraints.IResolveConstraint expected)
        => Assert.That(File.GetUnixFileMode(Path.Combine(root, relative)), expected, relative);

    private static void Add(ZipArchive zip, string name, int mode)
    {
        var entry = zip.CreateEntry(name);
        entry.ExternalAttributes = mode << 16;
        using var writer = new StreamWriter(entry.Open());
        writer.Write("x");
    }
}
