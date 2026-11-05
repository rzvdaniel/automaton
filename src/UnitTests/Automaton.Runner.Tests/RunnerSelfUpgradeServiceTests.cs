using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Tests;

[TestFixture]
public sealed class RunnerSelfUpgradeServiceTests
{
    private static readonly string Installation = $"sh_{Guid.NewGuid():N}";
    private readonly Guid runnerId = Guid.NewGuid();
    private readonly List<string> calls = [];

    [SetUp]
    public void Reset() => calls.Clear();

    private RunnerSelfUpgradeService Create(Func<IReadOnlyList<string>, (int, string)> docker, string? installation = null)
        => new(() => runnerId.ToString("D"), installation ?? Installation, (arguments, _) =>
        {
            calls.Add(string.Join(' ', arguments.Take(2)));
            return Task.FromResult(docker(arguments));
        });

    [Test]
    public async Task StartsAHelperFromItsOwnImageThatRunsTheServersScript()
    {
        IReadOnlyList<string>? run = null;
        var service = Create(arguments =>
        {
            if (arguments[0] == "run") run = arguments;
            return (0, arguments[0] == "inspect" ? "sha256:own\n" : "");
        });

        Assert.That((await service.StartAsync(new(runnerId, "echo upgrade"), default)).Started, Is.True);

        Assert.That(run, Is.Not.Null);
        Assert.That(run, Does.Contain($"automaton-self-hosted-{Installation[3..15]}-upgrade"));
        Assert.That(run, Does.Contain("sha256:own"));
        Assert.That(run![^1], Does.EndWith("echo upgrade"));
        Assert.That(run, Does.Contain("/var/run/docker.sock:/var/run/docker.sock"));
    }

    [Test]
    public void RefusesAnUpgradePreparedForAnotherRunner()
    {
        var service = Create(_ => (0, ""));
        Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(new(Guid.NewGuid(), "echo"), default));
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public void RefusesARunnerWithoutAnInstallationIdentity()
    {
        var service = Create(_ => (0, ""), installation: "sh_bad");
        Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(new(runnerId, "echo"), default));
    }

    [Test]
    public void ReportsAHelperThatDidNotStart()
    {
        var service = Create(arguments => arguments[0] == "run" ? (125, "no such image") : (0, "sha256:own"));
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(new(runnerId, "echo"), default));
        Assert.That(error!.Message, Does.Contain("no such image"));
    }

    [TestCase("running 0", "running")]
    [TestCase("exited 0", "succeeded")]
    [TestCase("exited 1", "failed")]
    public async Task ReadsTheHelperState(string state, string expected)
    {
        var service = Create(arguments => (0, arguments[0] == "inspect" ? state : "did not connect"));
        var status = await service.StatusAsync(default);
        Assert.That(status.State, Is.EqualTo(expected));
        Assert.That(status.Log, Is.EqualTo("did not connect"));
    }

    [Test]
    public async Task NoHelperMeansNoUpgradeWasStarted()
        => Assert.That((await Create(_ => (1, "Error: No such object")).StatusAsync(default)).State, Is.EqualTo("none"));

    [Test]
    public async Task TheCommandExecutorRoutesTheUpgradeCommands()
    {
        var upgrade = Create(arguments => (0, arguments[0] == "inspect" ? "exited 0" : ""));
        var executor = new AppActionCommandExecutor(null!, null!, null!, selfUpgrade: upgrade);
        var result = await executor.ExecuteAsync(AppActionCommand.Create("upgrade-status", new { }), default);
        Assert.That(result.ActionResult!.Value.GetProperty("State").GetString(), Is.EqualTo("succeeded"));
    }
}
