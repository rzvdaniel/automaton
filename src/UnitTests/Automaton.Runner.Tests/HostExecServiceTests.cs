using Automaton.Core.Models;
using NUnit.Framework;
using Automaton.Runner.Console.Services;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class HostExecServiceTests
{
    [Test]
    public void TheSecretIsNeverInTheArgumentsOnlyTheScript()
    {
        var args = HostExecService.Arguments("img", "name", "cat | install");
        Assert.That(args, Does.Contain("--privileged").And.Contain("cat | install"));
        Assert.That(args.Any(a => a.Contains("tok-secret")), Is.False);
        Assert.That(args, Does.Contain("-i"));
    }

    [Test]
    public async Task OutputThatRepeatsAnInputLineIsRedactedAndStdinIsPassedThrough()
    {
        string? seenStdin = null;
        var service = new HostExecService((file, args, stdin, timeout, onTimeout, token) =>
        {
            if (args[0] == "inspect") return Task.FromResult((0, "sha256:abc\n", "", false));
            seenStdin = stdin;
            return Task.FromResult((0, "token is tok-secret-1\nok", "bad tok-secret-1", false));
        });
        var result = await service.RunAsync(new HostExecRequest("cat", "tok-secret-1\nother-secret", 60), default);
        Assert.That(seenStdin, Is.EqualTo("tok-secret-1\nother-secret"));
        Assert.That(result.Output, Is.EqualTo("token is ***\nok"));
        Assert.That(result.Error, Is.EqualTo("bad ***"));
        Assert.That(result.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public async Task ATimeoutIsReportedAndStopsTheContainer()
    {
        var killed = false;
        var service = new HostExecService((file, args, stdin, timeout, onTimeout, token) =>
        {
            if (args[0] == "inspect") return Task.FromResult((0, "sha256:abc", "", false));
            if (args[0] == "kill") { killed = true; return Task.FromResult((0, "", "", false)); }
            return onTimeout().ContinueWith(_ => (-1, "partial", "", true));
        });
        var result = await service.RunAsync(new HostExecRequest("sleep 9999", null, 1), default);
        Assert.That(result.TimedOut, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(-1));
        Assert.That(killed, Is.True);
    }

    [Test]
    public async Task ABackgroundedChildIsGoneAfterATimeout()
    {
        const string image = "python:3.12-slim";
        if (await Docker("image", "inspect", image) != 0) Assert.Ignore("Docker or " + image + " is not available.");
        const string name = "automaton-host-exec-test-setsid";
        var args = HostExecService.Arguments(image, name, "setsid nohup sleep 4343 >/dev/null 2>&1 & sleep 600");
        var result = await HostExecService.RunProcessAsync("docker", args, "", TimeSpan.FromSeconds(4),
            () => HostExecService.RunProcessAsync("docker", ["kill", name], "", TimeSpan.FromSeconds(30), () => Task.CompletedTask, default), default);
        Assert.That(result.TimedOut, Is.True);
        await Task.Delay(2000);
        var left = await HostExecService.RunProcessAsync("pgrep", ["-x", "sleep", "-a"], "", TimeSpan.FromSeconds(10), () => Task.CompletedTask, default);
        Assert.That(left.Output, Does.Not.Contain("4343"));
    }

    private static async Task<int> Docker(params string[] args)
    {
        try { return (await HostExecService.RunProcessAsync("docker", args, "", TimeSpan.FromSeconds(30), () => Task.CompletedTask, default)).ExitCode; }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    [Test]
    public async Task ARealProcessIsStoppedAtItsTimeout()
    {
        var result = await HostExecService.RunProcessAsync("sleep", ["30"], "", TimeSpan.FromMilliseconds(300), () => Task.CompletedTask, default);
        Assert.That(result.TimedOut, Is.True);
    }

    [Test]
    public void BadRequestsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => HostExecService.Validate(new HostExecRequest("", null, 60)));
        Assert.Throws<InvalidDataException>(() => HostExecService.Validate(new HostExecRequest("id", null, 7201)));
    }
}
