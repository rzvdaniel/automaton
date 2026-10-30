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
    [TestCase("legacy-data", 1)]
    [TestCase("malformed-data", 1)]
    [TestCase("foreign-data", 0)]
    [TestCase("foreign-runner", 1)]
    [TestCase("foreign-installation", 1)]
    [TestCase("missing-config", 1)]
    [TestCase("stopped", 0)]
    public async Task HostCommandChecksOwnershipAndRefusesRemainingWorkloads(string mode, int expectedExitCode)
    {
        if (OperatingSystem.IsWindows()) return;
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
        Assert.That(calls, Does.Not.Contain("volume rm retained-app-data"));
        if (mode is "owned" or "stopped" or "foreign-data")
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
        start.Environment["FAKE_CONFIG"] = path;
        var persistedId = mode == "foreign-runner" ? Guid.NewGuid() : runnerId;
        File.WriteAllText(Path.Combine(path, "runner.json"), "{\"RunnerId\":\"" + persistedId + "\"}");
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
              *)
                if [ "$MODE" = foreign-installation ]; then echo 'AUTOMATON_RUNNER_INSTALLATION_ID=wrong-installation'; else echo "AUTOMATON_RUNNER_INSTALLATION_ID=$INSTALLATION"; fi ;;
            esac ;;
          cp*)
            if [ "$MODE" = missing-config ]; then exit 1; fi
            tar -cf - -C "$FAKE_CONFIG" runner.json ;;
          'volume ls')
            if [ "$MODE" != missing ] && [ "$3" = --format ]; then echo "automaton-machine-runner-$INSTALLATION-workspace"; fi
            case "$MODE" in data|legacy-data|malformed-data|foreign-data)
              if [ "$3" = --quiet ]; then echo retained-app-data; fi ;;
            esac ;;
          'volume inspect')
            case "$4" in
              *machine-runner-id*)
                case "$MODE" in
                  legacy-data) echo '<no value>' ;;
                  malformed-data) echo invalid ;;
                  foreign-data) echo '11111111-1111-1111-1111-111111111111' ;;
                  *) echo "$RUNNER_ID" ;;
                esac ;;
              *) echo "$INSTALLATION" ;;
            esac ;;
          'network ls')
            if [ "$MODE" != missing ]; then echo "automaton-self-hosted-$INSTALLATION-runtime"; fi ;;
        esac
        """;
}
