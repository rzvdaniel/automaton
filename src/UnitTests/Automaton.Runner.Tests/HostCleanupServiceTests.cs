using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

[TestFixture]
public sealed class HostCleanupServiceTests
{
    private readonly List<IReadOnlyList<string>> calls = [];

    [SetUp]
    public void Reset() => calls.Clear();

    private HostCleanupService Create(Func<IReadOnlyList<string>, (int, string)> docker)
        => new((arguments, _) => { calls.Add(arguments); return Task.FromResult(docker(arguments)); });

    private static (int, string) Healthy(IReadOnlyList<string> a) => a[0] switch
    {
        "image" => (0, "Total reclaimed space: 1.5GB"),
        "builder" => (0, "Total reclaimed space: 500MB"),
        "inspect" => (0, "sha256:own\n"),
        _ => (0, "freed=2000000")
    };

    [TestCase("Total reclaimed space: 1.8GB", 1_800_000_000L)]
    [TestCase("Total reclaimed space: 12.5kB", 12_500L)]
    [TestCase("Total reclaimed space: 0B", 0L)]
    [TestCase("nothing to say", 0L)]
    public void ReadsTheSpaceDockerReports(string output, long bytes)
        => Assert.That(HostCleanupService.ParseReclaimed(output), Is.EqualTo(bytes));

    [Test]
    public async Task PrunesOnlyOldUnusedImagesAndCacheAndNeverVolumes()
    {
        var result = await Create(Healthy).RunAsync(new(7, 200), default);

        Assert.That(result.BytesFreed, Is.EqualTo(1_500_000_000L + 500_000_000L + 2_000_000L));
        Assert.That(calls[0], Is.EqualTo(new[] { "image", "prune", "--all", "--force", "--filter", "until=168h" }));
        Assert.That(calls[1], Is.EqualTo(new[] { "builder", "prune", "--force", "--filter", "until=168h" }));
        Assert.That(calls.Any(c => c.Contains("volume") || c.Contains("system")), Is.False);
        Assert.That(calls[3], Does.Contain("JOURNAL_LIMIT_MB=200"));
    }

    [Test]
    public async Task ASkippedJournalDoesNotFailTheRun()
    {
        var result = await Create(a => a[0] == "run" ? (125, "no such directory") : Healthy(a)).RunAsync(new(3, 100), default);
        Assert.That(result.BytesFreed, Is.EqualTo(2_000_000_000L));
        Assert.That(result.Summary, Does.Contain("Journal: skipped"));
    }

    [Test]
    public void FailsWhenDockerFailsEverywhere()
        => Assert.ThrowsAsync<InvalidOperationException>(() => Create(_ => (1, "daemon down")).RunAsync(new(7, 200), default));

    [TestCase(0, 200)]
    [TestCase(7, 10)]
    public void RefusesSettingsOutOfRange(int days, int journal)
        => Assert.ThrowsAsync<InvalidDataException>(() => Create(Healthy).RunAsync(new(days, journal), default));

    [Test]
    public async Task TheExecutorRoutesTheCommand()
    {
        var executor = new AppActionCommandExecutor(null!, null!, null!, hostCleanup: Create(Healthy));
        var result = await executor.ExecuteAsync(AppActionCommand.Create("host-cleanup", new HostCleanupRequest(7, 200)), default);
        Assert.That(result.ActionResult!.Value.GetProperty("BytesFreed").GetInt64(), Is.GreaterThan(0));
    }
}
