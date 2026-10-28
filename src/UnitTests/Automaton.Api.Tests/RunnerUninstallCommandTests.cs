using System.Diagnostics;
using Automaton.Api.Services;

namespace Automaton.Api.Tests;

public sealed class RunnerUninstallCommandTests
{
    [TestCase(null)]
    [TestCase("sh_invalid")]
    [TestCase("sh_'; docker system prune")]
    public void RejectsInvalidInstallationIdentity(string? installation)
        => Assert.Throws<InvalidOperationException>(() => RunnerUninstallCommand.Build(Guid.NewGuid(), installation));

    [TestCase("owned", 0)]
    [TestCase("missing", 0)]
    [TestCase("orphan", 1)]
    [TestCase("foreign", 1)]
    [TestCase("data", 1)]
    public async Task HostCommandChecksOwnershipAndRefusesRemainingWorkloads(string mode, int expectedExitCode)
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("The Machine Management host command requires a Unix shell.");
        using var directory = new AppAuthorizationTestData.TemporaryDirectory();
        var runnerId = Guid.NewGuid();
        var installation = $"sh_{Guid.NewGuid():N}";
        var log = Path.Combine(directory.Path, "docker-calls");
        var docker = Path.Combine(directory.Path, "docker");
        await File.WriteAllTextAsync(docker, FakeDocker);
        File.SetUnixFileMode(docker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var process = StartShell(directory.Path, log, mode, installation, runnerId);
        await process.StandardInput.WriteAsync(RunnerUninstallCommand.Build(runnerId, installation));
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        var calls = await File.ReadAllTextAsync(log);

        Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode), await output + await error);
        if (mode == "owned")
        {
            Assert.That(calls, Does.Contain($"rm --force automaton-self-hosted-{installation[3..15]}"));
            Assert.That(calls, Does.Contain($"rm --force automaton-static-{installation[3..]}"));
            Assert.That(calls, Does.Contain($"volume rm automaton-machine-runner-{installation}-workspace"));
        }
        else
        {
            Assert.That(calls, Does.Not.Contain("rm --force"));
            Assert.That(calls, Does.Not.Contain("volume rm"));
        }
    }

    private static Process StartShell(string path, string log, string mode, string installation, Guid runnerId)
    {
        var start = new ProcessStartInfo("/bin/sh") { RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["PATH"] = path + ":" + Environment.GetEnvironmentVariable("PATH");
        start.Environment["CALLS"] = log;
        start.Environment["MODE"] = mode;
        start.Environment["INSTALLATION"] = installation;
        start.Environment["RUNNER_ID"] = runnerId.ToString("D");
        return Process.Start(start)!;
    }

    private const string FakeDocker = """
        #!/bin/sh
        set -eu
        printf '%s\n' "$*" >> "$CALLS"
        case "$1 ${2-}" in
          'ps -aq')
            if [ "$MODE" = orphan ]; then echo orphan-container; fi ;;
          'ps -a')
            if [ "$MODE" != missing ]; then
              printf 'automaton-self-hosted-%.12s\n' "${INSTALLATION#sh_}"
              printf 'automaton-static-%s\n' "${INSTALLATION#sh_}"
            fi ;;
          'inspect --format')
            case "$3" in
              *machine-runner-id*)
                if [ "$MODE" = foreign ]; then echo foreign-runner; else echo "$RUNNER_ID"; fi ;;
              *automaton.resource*) echo machine-static-server ;;
              *) echo "AUTOMATON_RUNNER_INSTALLATION_ID=$INSTALLATION" ;;
            esac ;;
          'volume ls')
            if [ "$MODE" != missing ] && [ "$3" = --format ]; then echo "automaton-machine-runner-$INSTALLATION-workspace"; fi
            if [ "$MODE" = data ] && [ "$3" = --quiet ]; then echo retained-app-data; fi ;;
          'volume inspect') echo "$INSTALLATION" ;;
          'network ls')
            if [ "$MODE" != missing ]; then echo "automaton-self-hosted-$INSTALLATION-runtime"; fi ;;
        esac
        """;
}
