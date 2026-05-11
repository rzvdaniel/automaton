using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities;
using System.Globalization;

namespace Automaton.Studio.Server.Services;

public static class HostedRunnerDockerCommandBuilder
{
    public static string BuildPreview(HostedRunner runner, HostedRunnerOptions options)
    {
        var cpu = runner.CpuCores.ToString("0.##", CultureInfo.InvariantCulture);
        var dataRoot = TrimTrailingSlash(options.DataRoot);

        return string.Join(" \\\n  ", new[]
        {
            "docker run -d",
            $"--name {Quote(runner.ContainerName)}",
            "--restart unless-stopped",
            $"--cpus {Quote(cpu)}",
            $"--memory {Quote($"{runner.MemoryMb}m")}",
            $"--network {Quote(options.DockerNetwork)}",
            $"-e AUTOMATON_SERVER_URL={Quote(options.ServerUrl)}",
            $"-e AUTOMATON_API_KEY={Quote("*** generated runner key ***")}",
            $"-e AUTOMATON_RUNNER_NAME={Quote(runner.Name)}",
            $"-e AUTOMATON_HOSTED_RUNNER_ID={Quote(runner.Id.ToString())}",
            $"-v {Quote($"{dataRoot}/{runner.Id}/config:/home/automaton/.config/AutomatonRunner")}",
            $"-v {Quote($"{dataRoot}/{runner.Id}/data:/app/data")}",
            $"-v {Quote($"{dataRoot}/{runner.Id}/logs:/app/logs")}",
            Quote(options.DockerImage)
        });
    }

    private static string TrimTrailingSlash(string value) => value.TrimEnd('/', '\\');

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
