using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities;
using System.Globalization;

namespace Automaton.Studio.Server.Services;

public static class HostedRunnerDockerCommandBuilder
{
    public static string BuildPreview(HostedRunner runner, HostedRunnerOptions options)
        => BuildProvisionScript(runner, options, "*** generated runner key ***");

    public static string BuildProvisionScript(HostedRunner runner, HostedRunnerOptions options, string apiKey)
    {
        var cpu = runner.CpuCores.ToString("0.##", CultureInfo.InvariantCulture);
        var dataRoot = TrimTrailingSlash(options.DataRoot);
        var runnerRoot = $"{dataRoot}/{runner.Id}";

        var dockerRun = string.Join(" \\\n  ", new[]
        {
            "docker run -d",
            $"--name {ShellQuote(runner.ContainerName)}",
            "--restart unless-stopped",
            $"--cpus {ShellQuote(cpu)}",
            $"--memory {ShellQuote($"{runner.MemoryMb}m")}",
            $"--network {ShellQuote(options.DockerNetwork)}",
            $"-e AUTOMATON_SERVER_URL={ShellQuote(options.ServerUrl)}",
            $"-e AUTOMATON_API_KEY={ShellQuote(apiKey)}",
            $"-e AUTOMATON_RUNNER_NAME={ShellQuote(runner.Name)}",
            $"-e AUTOMATON_HOSTED_RUNNER_ID={ShellQuote(runner.Id.ToString())}",
            $"-v {ShellQuote($"{runnerRoot}/config:/home/automaton/.config/AutomatonRunner")}",
            $"-v {ShellQuote($"{runnerRoot}/data:/app/data")}",
            $"-v {ShellQuote($"{runnerRoot}/logs:/app/logs")}",
            ShellQuote(options.DockerImage)
        });

        return string.Join("\n", new[]
        {
            "set -eu",
            $"mkdir -p {ShellQuote($"{runnerRoot}/config")} {ShellQuote($"{runnerRoot}/data")} {ShellQuote($"{runnerRoot}/logs")}",
            $"chown -R 1000:1000 {ShellQuote($"{runnerRoot}/config")} {ShellQuote($"{runnerRoot}/data")} {ShellQuote($"{runnerRoot}/logs")}",
            $"docker network inspect {ShellQuote(options.DockerNetwork)} >/dev/null 2>&1 || docker network create {ShellQuote(options.DockerNetwork)} >/dev/null",
            $"docker rm -f {ShellQuote(runner.ContainerName)} >/dev/null 2>&1 || true",
            dockerRun
        });
    }

    private static string TrimTrailingSlash(string value) => value.TrimEnd('/', '\\');

    internal static string ShellQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";
}
