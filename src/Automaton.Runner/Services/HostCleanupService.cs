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

    /// <summary>
    /// Installs the pending security updates (only the origins Ubuntu's unattended-upgrades is set to, so no other updates) on the
    /// host. The fixed argument list runs in the host's namespaces through a short-lived privileged container; no shell, no input.
    /// </summary>
    public async Task<HostOsUpdateResult> OsUpdateAsync(CancellationToken token)
    {
        var image = await OwnImageAsync(token);
        var update = await run(HostCommand(image, "unattended-upgrade", "-v"), token);
        var restart = await run(HostCommand(image, "test", "-e", "/var/run/reboot-required"), token);
        return new(update.ExitCode == 0, Tail(update.Output), restart.ExitCode == 0);
    }

    /// <summary>Schedules a restart of the host one minute from now (so this answer is delivered first); the Runner container restarts with Docker.</summary>
    public async Task<HostRebootResult> RebootAsync(CancellationToken token)
    {
        var image = await OwnImageAsync(token);
        var result = await run(HostCommand(image, "shutdown", "-r", "+1", "Restart requested from Server Console"), token);
        if (result.ExitCode != 0) throw new InvalidOperationException("The restart could not be scheduled: " + Tail(result.Output));
        return new(true);
    }

    /// <summary>Lifts the ban on one address in every jail. The address is validated; the fixed argument list runs without a shell.</summary>
    public async Task<Fail2BanActionResult> Fail2BanUnbanAsync(Fail2BanAddressRequest request, CancellationToken token)
    {
        var ip = ValidAddress(request.Ip);
        var image = await OwnImageAsync(token);
        var changed = new List<string>();
        foreach (var jail in await Fail2BanJailsAsync(image, token))
            if ((await run(HostCommand(image, "fail2ban-client", "set", jail, "unbanip", ip), token)).ExitCode == 0) changed.Add(jail);
        if (changed.Count == 0) throw new InvalidOperationException("fail2ban did not unban the address; it may not be banned.");
        return new(ip, changed);
    }

    /// <summary>
    /// Adds one address to every running jail's allowlist (<c>addignoreip</c>) and lifts its ban. This is fail2ban's live allowlist:
    /// it lasts until fail2ban reloads or restarts (an installer update reloads it); a permanent one goes into <c>jail.local</c>.
    /// </summary>
    public async Task<Fail2BanActionResult> Fail2BanAllowlistAsync(Fail2BanAddressRequest request, CancellationToken token)
    {
        var ip = ValidAddress(request.Ip);
        var image = await OwnImageAsync(token);
        var changed = new List<string>();
        foreach (var jail in await Fail2BanJailsAsync(image, token))
            if ((await run(HostCommand(image, "fail2ban-client", "set", jail, "addignoreip", ip), token)).ExitCode == 0)
            {
                changed.Add(jail);
                await run(HostCommand(image, "fail2ban-client", "set", jail, "unbanip", ip), token);
            }
        if (changed.Count == 0) throw new InvalidOperationException("fail2ban did not allowlist the address.");
        return new(ip, changed);
    }

    /// <summary>One IPv4 or IPv6 address in its own canonical form; anything else (a name, a range, an option) is refused.</summary>
    internal static string ValidAddress(string? value)
    {
        if (!System.Net.IPAddress.TryParse(value, out var address) || value!.Contains('%') || value.Length > 45 || value.Any(char.IsWhiteSpace))
            throw new InvalidDataException("Give one IPv4 or IPv6 address.");
        return address.ToString();
    }

    private async Task<IReadOnlyList<string>> Fail2BanJailsAsync(string image, CancellationToken token)
    {
        var status = await run(HostCommand(image, "fail2ban-client", "status"), token);
        if (status.ExitCode != 0) throw new InvalidOperationException("fail2ban is not running on this machine.");
        return ParseJails(status.Output);
    }

    /// <summary>Reads the jail names from the "Jail list:" line of <c>fail2ban-client status</c>.</summary>
    internal static IReadOnlyList<string> ParseJails(string output)
    {
        var line = output.Split('\n').FirstOrDefault(item => item.Contains("Jail list:", StringComparison.Ordinal));
        return line is null ? [] : line[(line.IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.Length is > 0 and <= 100 && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')).ToArray();
    }

    private async Task<string> OwnImageAsync(CancellationToken token)
    {
        var own = await run(["inspect", "--format", "{{.Image}}", Environment.MachineName], token);
        if (own.ExitCode != 0) throw new InvalidOperationException("This Runner cannot inspect its own container, so it cannot reach the host.");
        return own.Output.Trim();
    }

    internal static IReadOnlyList<string> HostCommand(string image, params string[] command)
        => ["run", "--rm", "--privileged", "--pid=host", "--user", "root", "--entrypoint", "nsenter", image,
            "-t", "1", "-m", "-u", "-i", "-n", "-p", "--", .. command];

    /// <summary>What Docker uses on this machine, from <c>docker system df</c>; read-only.</summary>
    public async Task<HostDiskReport> DiskReportAsync(CancellationToken token)
    {
        var result = await run(["system", "df", "--format", "{{.Type}}|{{.Size}}|{{.Reclaimable}}"], token);
        if (result.ExitCode != 0) throw new InvalidOperationException("Docker could not report its disk use: " + Tail(result.Output));
        return ParseDiskReport(result.Output);
    }

    /// <summary>Reads the "Type|Size|Reclaimable" lines of <c>docker system df</c>; unknown lines are ignored.</summary>
    internal static HostDiskReport ParseDiskReport(string output)
    {
        long images = 0, containers = 0, volumes = 0, cache = 0, reclaimable = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length != 3) continue;
            var size = ParseReclaimed("Total reclaimed space: " + parts[1]);
            var free = ParseReclaimed("Total reclaimed space: " + parts[2].Split(' ')[0]);
            switch (parts[0])
            {
                case "Images": images = size; break;
                case "Containers": containers = size; break;
                case "Local Volumes": volumes = size; break;
                case "Build Cache": cache = size; break;
                default: continue;
            }
            reclaimable += free;
        }
        return new(images, containers, volumes, cache, reclaimable);
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
