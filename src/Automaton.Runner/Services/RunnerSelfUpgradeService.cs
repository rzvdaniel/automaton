using System.Diagnostics;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Upgrades this Runner from the inside: a Runner cannot replace its own container, so it starts a short-lived
/// helper container from its current image. The helper runs the server's upgrade script, which stops and renames this
/// container, starts the new one and rolls back if it does not connect.
/// </summary>
internal sealed class RunnerSelfUpgradeService(Func<string> runnerId, string? installationId, RunnerSelfUpgradeService.DockerRunner? docker = null)
{
    internal delegate Task<(int ExitCode, string Output)> DockerRunner(IReadOnlyList<string> arguments, CancellationToken token);

    private readonly DockerRunner run = docker ?? RunDockerAsync;

    public async Task<UpgradeRunnerStarted> StartAsync(UpgradeRunnerRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(runnerId(), out var own) || own != request.RunnerId)
            throw new InvalidOperationException("The upgrade was prepared for another Runner.");
        var helper = HelperName();
        var image = await run(["inspect", "--format", "{{.Image}}", Environment.MachineName], token);
        if (image.ExitCode != 0)
            throw new InvalidOperationException("This Runner is not running in a Docker container it can inspect.");
        await run(["rm", "--force", helper], token);
        // A short pause lets this command's result reach the server before the old container stops.
        var script = "sleep 5\n" + request.Script;
        var started = await run(["run", "-d", "--name", helper, "--user", "root",
            "-v", "/var/run/docker.sock:/var/run/docker.sock", "--entrypoint", "/bin/sh", image.Output.Trim(), "-c", script], token);
        if (started.ExitCode != 0)
            throw new InvalidOperationException("The upgrade helper container did not start: " + Tail(started.Output));
        return new(true);
    }

    public async Task<RunnerUpgradeHelperStatus> StatusAsync(CancellationToken token)
    {
        var helper = HelperName();
        var state = await run(["inspect", "--format", "{{.State.Status}} {{.State.ExitCode}}", helper], token);
        if (state.ExitCode != 0) return new("none", null);
        var parts = state.Output.Trim().Split(' ');
        var log = await run(["logs", "--tail", "20", helper], token);
        var text = Tail(log.Output);
        return parts[0] switch
        {
            "running" or "created" => new("running", text),
            "exited" when parts.Length > 1 && parts[1] == "0" => new("succeeded", text),
            _ => new("failed", text)
        };
    }

    private string HelperName()
    {
        var installation = installationId ?? string.Empty;
        if (installation.Length != 35 || !installation.StartsWith("sh_", StringComparison.Ordinal))
            throw new InvalidOperationException("This Runner has no Machine Management installation identity.");
        return $"automaton-self-hosted-{installation[3..15]}-upgrade";
    }

    private static string Tail(string text) => text.Length <= 2000 ? text.Trim() : text[^2000..].Trim();

    private static async Task<(int, string)> RunDockerAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo("docker")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the Docker CLI.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        await process.WaitForExitAsync(limit.Token);
        return (process.ExitCode, await output + await error);
    }
}
