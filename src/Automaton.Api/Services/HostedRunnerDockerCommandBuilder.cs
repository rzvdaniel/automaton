using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using System.Globalization;

namespace Automaton.Api.Services;

public static class HostedRunnerDockerCommandBuilder
{
    public static string BuildPreview(HostedRunner runner, RunnerHost host, HostedRunnerOptions options)
    {
        return string.Equals(options.DeploymentMode, HostedRunnerDeploymentModes.LocalDocker, StringComparison.Ordinal)
               || string.Equals(host.Provider, RunnerHostProviders.Local, StringComparison.OrdinalIgnoreCase)
            ? BuildLocalDockerProvisionScript(runner, options, "*** generated runner key ***")
            : BuildSshDockerProvisionScript(runner, options, "*** generated runner key ***");
    }

    public static string BuildProvisionScript(HostedRunner runner, RunnerHost host, HostedRunnerOptions options, string apiKey)
    {
        return string.Equals(options.DeploymentMode, HostedRunnerDeploymentModes.LocalDocker, StringComparison.Ordinal)
               || string.Equals(host.Provider, RunnerHostProviders.Local, StringComparison.OrdinalIgnoreCase)
            ? BuildLocalDockerProvisionScript(runner, options, apiKey)
            : BuildSshDockerProvisionScript(runner, options, apiKey);
    }

    private static string BuildSshDockerProvisionScript(HostedRunner runner, HostedRunnerOptions options, string apiKey)
    {
        var cpu = runner.CpuCores.ToString("0.##", CultureInfo.InvariantCulture);
        var dataRoot = TrimTrailingSlash(options.DataRoot);
        var runnerRoot = $"{dataRoot}/{runner.Id}";
        var dataVolumeName = BuildWorkspaceVolumeName(runner);

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
            BuildRuntimePortPublishArgument(runner, options),
            $"-v {ShellQuote($"{runnerRoot}/config:/home/automaton/.config/AutomatonRunner")}",
            $"-v {ShellQuote($"{dataVolumeName}:/automaton/workspace")}",
            $"-v {ShellQuote($"{runnerRoot}/logs:/app/logs")}",
            ShellQuote(options.DockerImage)
        }.Where(argument => !string.IsNullOrWhiteSpace(argument)));

        var runtimeNetworkAttach = string.IsNullOrWhiteSpace(options.RuntimeDockerNetwork)
            ? null
            : $"docker network inspect {ShellQuote(options.RuntimeDockerNetwork)} >/dev/null 2>&1 || docker network create {ShellQuote(options.RuntimeDockerNetwork)} >/dev/null";

        var runtimeNetworkConnect = string.IsNullOrWhiteSpace(options.RuntimeDockerNetwork) ||
                                    string.Equals(options.RuntimeDockerNetwork, options.DockerNetwork, StringComparison.Ordinal)
            ? null
            : $"docker network connect {ShellQuote(options.RuntimeDockerNetwork)} {ShellQuote(runner.ContainerName)} >/dev/null 2>&1 || true";

        var lines = new List<string>
        {
            "set -eu",
            $"mkdir -p {ShellQuote($"{runnerRoot}/config")} {ShellQuote($"{runnerRoot}/logs")}",
            $"chown -R 1000:1000 {ShellQuote($"{runnerRoot}/config")} {ShellQuote($"{runnerRoot}/logs")}",
            $"docker volume create {ShellQuote(dataVolumeName)} >/dev/null",
            $"docker network inspect {ShellQuote(options.DockerNetwork)} >/dev/null 2>&1 || docker network create {ShellQuote(options.DockerNetwork)} >/dev/null",
            $"docker rm -f {ShellQuote(runner.ContainerName)} >/dev/null 2>&1 || true",
            dockerRun
        };

        if (!string.IsNullOrWhiteSpace(runtimeNetworkAttach))
        {
            lines.Insert(4, runtimeNetworkAttach);
        }

        if (!string.IsNullOrWhiteSpace(runtimeNetworkConnect))
        {
            lines.Add(runtimeNetworkConnect);
        }

        return string.Join("\n", lines);
    }

    private static string BuildLocalDockerProvisionScript(HostedRunner runner, HostedRunnerOptions options, string apiKey)
    {
        var cpu = runner.CpuCores.ToString("0.##", CultureInfo.InvariantCulture);
        var configVolumeName = BuildConfigVolumeName(runner);
        var workspaceVolumeName = BuildWorkspaceVolumeName(runner);
        var logsVolumeName = BuildLogsVolumeName(runner);

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
            BuildRuntimePortPublishArgument(runner, options),
            $"-v {ShellQuote($"{configVolumeName}:/home/automaton/.config/AutomatonRunner")}",
            $"-v {ShellQuote($"{workspaceVolumeName}:/automaton/workspace")}",
            $"-v {ShellQuote($"{logsVolumeName}:/app/logs")}",
            ShellQuote(options.DockerImage)
        }.Where(argument => !string.IsNullOrWhiteSpace(argument)));

        var runtimeNetworkAttach = string.IsNullOrWhiteSpace(options.RuntimeDockerNetwork)
            ? null
            : $"docker network inspect {ShellQuote(options.RuntimeDockerNetwork)} >/dev/null 2>&1 || docker network create {ShellQuote(options.RuntimeDockerNetwork)} >/dev/null";

        var runtimeNetworkConnect = string.IsNullOrWhiteSpace(options.RuntimeDockerNetwork) ||
                                    string.Equals(options.RuntimeDockerNetwork, options.DockerNetwork, StringComparison.Ordinal)
            ? null
            : $"docker network connect {ShellQuote(options.RuntimeDockerNetwork)} {ShellQuote(runner.ContainerName)} >/dev/null 2>&1 || true";

        var lines = new List<string>
        {
            "set -eu",
            $"docker volume create {ShellQuote(configVolumeName)} >/dev/null",
            $"docker volume create {ShellQuote(workspaceVolumeName)} >/dev/null",
            $"docker volume create {ShellQuote(logsVolumeName)} >/dev/null",
            $"docker network inspect {ShellQuote(options.DockerNetwork)} >/dev/null 2>&1 || docker network create {ShellQuote(options.DockerNetwork)} >/dev/null",
            $"docker rm -f {ShellQuote(runner.ContainerName)} >/dev/null 2>&1 || true",
            dockerRun
        };

        if (!string.IsNullOrWhiteSpace(runtimeNetworkAttach))
        {
            lines.Insert(4, runtimeNetworkAttach);
        }

        if (!string.IsNullOrWhiteSpace(runtimeNetworkConnect))
        {
            lines.Add(runtimeNetworkConnect);
        }

        return string.Join("\n", lines);
    }

    internal static string BuildConfigVolumeName(HostedRunner runner) => $"automaton-runner-{runner.Id:N}-config";

    internal static string BuildWorkspaceVolumeName(HostedRunner runner) => $"automaton-runner-{runner.Id:N}-data";

    internal static string BuildLogsVolumeName(HostedRunner runner) => $"automaton-runner-{runner.Id:N}-logs";

    private static string BuildRuntimePortPublishArgument(HostedRunner runner, HostedRunnerOptions options)
        => runner.RuntimePort is > 0
            ? $"-p {ShellQuote($"{runner.RuntimePort}:{options.RuntimePort}")}"
            : string.Empty;

    private static string TrimTrailingSlash(string value) => value.TrimEnd('/', '\\');

    internal static string ShellQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";
}
