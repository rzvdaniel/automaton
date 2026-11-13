using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// The nightly host cleanup, run on the server's request: unused Docker images and build cache older than N days, and archived
/// journal files above a size limit. Never volumes, images that a container (even a stopped one) still uses, or backups.
/// </summary>
internal sealed partial class HostCleanupService(HostCleanupService.DockerRunner? docker = null)
{
    internal delegate Task<(int ExitCode, string Output)> DockerRunner(IReadOnlyList<string> arguments, CancellationToken token);

    private const string JournalDirectory = "/var/log/journal";
    // Removes the oldest archived journal files (never the active ones) until the directory fits the limit, like journalctl --vacuum-size.
    private const string JournalScript =
        "limit=$((JOURNAL_LIMIT_MB*1024*1024)); total=$(du -sb /host-journal | cut -f1); freed=0\n" +
        "for f in $(find /host-journal -type f -name '*@*.journal' -printf '%T@ %p\\n' | sort -n | cut -d' ' -f2-); do\n" +
        "  [ \"$total\" -le \"$limit\" ] && break\n" +
        "  s=$(stat -c %s \"$f\"); rm -f -- \"$f\" && { total=$((total-s)); freed=$((freed+s)); }\n" +
        "done\n" +
        "echo \"freed=$freed\"";

    private readonly DockerRunner run = docker ?? RunDockerAsync;

    public async Task<HostCleanupResult> RunAsync(HostCleanupRequest request, CancellationToken token)
    {
        if (request.KeepDays is < 1 or > 365 || request.JournalLimitMb is < 50 or > 100_000)
            throw new InvalidDataException("The cleanup settings are out of range.");
        var until = $"{request.KeepDays * 24}h";
        var steps = new List<string>();
        long freed = 0;

        var images = await run(["image", "prune", "--all", "--force", "--filter", $"until={until}"], token);
        freed += Step("Docker images", images, steps);
        var cache = await run(["builder", "prune", "--force", "--filter", $"until={until}"], token);
        freed += Step("Build cache", cache, steps);

        var own = await run(["inspect", "--format", "{{.Image}}", Environment.MachineName], token);
        if (own.ExitCode != 0)
            steps.Add("Journal: skipped, this Runner cannot inspect its own container.");
        else
        {
            var journal = await run(["run", "--rm", "--user", "root", "--entrypoint", "/bin/sh",
                "--mount", $"type=bind,source={JournalDirectory},target=/host-journal",
                "-e", $"JOURNAL_LIMIT_MB={request.JournalLimitMb}", own.Output.Trim(), "-c", JournalScript], token);
            var match = JournalFreed().Match(journal.Output);
            if (journal.ExitCode == 0 && match.Success)
            {
                var bytes = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                freed += bytes;
                steps.Add($"Journal: freed {Format(bytes)}.");
            }
            else steps.Add("Journal: skipped, the host has no persistent journal to trim.");
        }

        if (images.ExitCode != 0 && cache.ExitCode != 0)
            throw new InvalidOperationException("Docker cleanup failed: " + Tail(images.Output));
        return new(freed, string.Join(' ', steps));
    }

    private static long Step(string name, (int ExitCode, string Output) result, List<string> steps)
    {
        if (result.ExitCode != 0)
        {
            steps.Add($"{name}: failed ({Tail(result.Output)}).");
            return 0;
        }
        var bytes = ParseReclaimed(result.Output);
        steps.Add($"{name}: freed {Format(bytes)}.");
        return bytes;
    }

    /// <summary>Reads "Total reclaimed space: 1.234GB" from a Docker prune command (units B, kB, MB, GB, TB).</summary>
    internal static long ParseReclaimed(string output)
    {
        var match = Reclaimed().Match(output);
        if (!match.Success) return 0;
        var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var factor = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => 1e3, "MB" => 1e6, "GB" => 1e9, "TB" => 1e12, _ => 1d
        };
        return (long)(value * factor);
    }

    private static string Format(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1e9:0.0} GB",
        >= 1_000_000 => $"{bytes / 1e6:0.0} MB",
        _ => $"{bytes / 1e3:0.0} kB"
    };

    private static string Tail(string text) => text.Length <= 300 ? text.Trim() : text[^300..].Trim();

    [GeneratedRegex(@"Total reclaimed space:\s*([0-9.]+)\s*([kKMGT]?B)")]
    private static partial Regex Reclaimed();

    [GeneratedRegex(@"freed=(\d+)")]
    private static partial Regex JournalFreed();

    private static async Task<(int, string)> RunDockerAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo("docker")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the Docker CLI.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromMinutes(15));
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var error = process.StandardError.ReadToEndAsync(limit.Token);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
        return (process.ExitCode, await output + await error);
    }
}
