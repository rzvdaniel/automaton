using System.Diagnostics;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public sealed class RunnerUpgradeCommandTests
{
    private const string Image = "ghcr.io/rzvdaniel/automaton-runner@sha256:9c03fb8f4b85f227ed741fd7e5e51e0fddc64a9a700725900ce8ca4ffd226338";

    [TestCase(null)]
    [TestCase("sh_invalid")]
    [TestCase("sh_'; docker system prune")]
    public void RejectsInvalidInstallationIdentity(string? installation)
        => Assert.Throws<InvalidOperationException>(() => RunnerUpgradeCommand.Build(Guid.NewGuid(), installation, Image));

    [TestCase("")]
    [TestCase("image'; rm -rf /")]
    [TestCase("Image:Latest")]
    public void RejectsUnsafeImageReference(string image)
        => Assert.Throws<InvalidOperationException>(() => RunnerUpgradeCommand.Build(Guid.NewGuid(), $"sh_{Guid.NewGuid():N}", image));

    [Test]
    public void TargetsTheInstallationContainerAndKeepsItsIdentity()
    {
        var runnerId = Guid.NewGuid();
        var installation = $"sh_{Guid.NewGuid():N}";
        var command = RunnerUpgradeCommand.Build(runnerId, installation, Image);

        Assert.That(command, Does.Contain($"container='automaton-self-hosted-{installation[3..15]}'"));
        Assert.That(command, Does.Contain($"AUTOMATON_RUNNER_INSTALLATION_ID={installation}"));
        Assert.That(command, Does.Contain(runnerId.ToString("D")));
        Assert.That(command, Does.Contain("restoring the previous container"));
    }

    [Test]
    public async Task GeneratedScriptIsValidShell()
    {
        if (OperatingSystem.IsWindows()) return;
        var start = new ProcessStartInfo("/bin/sh", "-n") { RedirectStandardInput = true,
            RedirectStandardError = true, UseShellExecute = false };
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(RunnerUpgradeCommand.Build(Guid.NewGuid(), $"sh_{Guid.NewGuid():N}", Image));
        process.StandardInput.Close();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.That(process.ExitCode, Is.EqualTo(0), error);
    }
}
