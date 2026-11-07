using System.Diagnostics;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Reads host figures from the Runner container (/proc is host-wide), the Docker API on the mounted socket, and a
/// short-lived read-only helper container for the few host files the Runner cannot see. Nothing here is exposed to apps.
/// </summary>
public sealed class HostHealthCollector
{
    private const string DockerSocket = "/var/run/docker.sock";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    private const string HelperScript =
        "echo '[os]'; cat /host/os-release 2>/dev/null; " +
        "echo '[updates]'; cat /host/updates/updates-available 2>/dev/null; " +
        "if [ -e /host/reboot-required ]; then echo '[reboot]'; stat -c %Y /host/reboot-required; fi";

    public async Task<MachineHostHealth> CollectAsync(CancellationToken token)
    {
        var (cpus, cpuPercent) = await ReadCpuAsync(token);
        var load = HostHealthParsers.ParseLoad(Read("/proc/loadavg"));
        var memory = HostHealthParsers.ParseMemory(Read("/proc/meminfo"));
        var disk = ReadDisk();
        var docker = HostHealthParsers.ParseDockerDiskUsage(await DockerDiskUsageAsync(token));
        var files = HostFiles.From(await ReadHostFilesAsync(token));

        return new MachineHostHealth(
            DateTime.UtcNow, files.OsName, files.OsVersion, HostHealthParsers.ParseUptimeSeconds(Read("/proc/uptime")),
            cpus, cpuPercent, load?.Load1, load?.Load5, load?.Load15, memory?.Total, memory?.Available,
            disk?.Total, disk?.Available, docker?.Images, docker?.Containers, docker?.Volumes, docker?.BuildCache,
            files.Pending, files.Security, files.RebootRequired, files.RebootSince,
            typeof(HostHealthCollector).Assembly.GetName().Version?.ToString());
    }

    private static async Task<(int? Cpus, double? Percent)> ReadCpuAsync(CancellationToken token)
    {
        var first = HostHealthParsers.ParseCpuStat(Read("/proc/stat"));
        await Task.Delay(TimeSpan.FromSeconds(1), token);
        var second = HostHealthParsers.ParseCpuStat(Read("/proc/stat"));
        if (second is not { } after) return (null, null);
        return (after.Cpus, first is { } before ? HostHealthParsers.CpuPercent(before, after) : null);
    }

    private sealed record HostFiles(
        string? OsName, string? OsVersion, int? Pending, int? Security, bool? RebootRequired, DateTime? RebootSince)
    {
        public static HostFiles From(Dictionary<string, string> sections)
        {
            var (name, version) = HostHealthParsers.ParseOsRelease(sections.GetValueOrDefault("os"));
            var (pending, security) = HostHealthParsers.ParseUpdatesAvailable(sections.GetValueOrDefault("updates"));
            DateTime? since = long.TryParse(sections.GetValueOrDefault("reboot")?.Trim(), out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime : null;
            // The helper container only ran when the os section exists; otherwise "restart required" is unknown.
            return new(name, version, pending, security, sections.ContainsKey("os") ? sections.ContainsKey("reboot") : null, since);
        }
    }

    private static string? Read(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static (long Total, long Available)? ReadDisk()
    {
        foreach (var path in new[] { "/automaton/storage", "/" })
        {
            try
            {
                var volume = new DriveInfo(path);
                if (volume.IsReady && volume.TotalSize > 0) return (volume.TotalSize, volume.AvailableFreeSpace);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException) { }
        }
        return null;
    }

    private static async Task<string?> DockerDiskUsageAsync(CancellationToken token)
    {
        var result = await RunAsync("curl",
            ["--silent", "--fail", "--max-time", "50", "--unix-socket", DockerSocket, "http://localhost/system/df"], token);
        return result?.ExitCode == 0 ? result.Output : null;
    }

    // Each attempt mounts fewer host paths: docker refuses a bind mount whose source is missing (no reboot-required file
    // when no restart is pending, no update-notifier on Debian).
    private static async Task<Dictionary<string, string>> ReadHostFilesAsync(CancellationToken token)
    {
        var image = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_IMAGE");
        if (string.IsNullOrWhiteSpace(image)) return [];
        var os = ("/etc/os-release", "/host/os-release");
        var reboot = ("/run/reboot-required", "/host/reboot-required");
        var updates = ("/var/lib/update-notifier", "/host/updates");
        (string Source, string Target)[][] attempts = [[os, reboot, updates], [os, reboot], [os, updates], [os]];
        foreach (var mounts in attempts)
        {
            var args = new List<string>
            {
                "run", "--rm", "--network", "none", "--read-only", "--cap-drop", "ALL", "--user", "65534:65534",
                "--security-opt", "no-new-privileges", "--pull", "never", "--entrypoint", "sh"
            };
            foreach (var (source, target) in mounts)
                args.AddRange(["--mount", $"type=bind,source={source},target={target},readonly"]);
            args.AddRange([image, "-c", HelperScript]);
            var result = await RunAsync("docker", args, token);
            if (result?.ExitCode == 0) return HostHealthParsers.SplitSections(result.Output);
        }
        return [];
    }

    private sealed record CommandResult(int ExitCode, string Output);

    private static async Task<CommandResult?> RunAsync(string file, IEnumerable<string> arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            using var process = Process.Start(info);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new CommandResult(process.ExitCode, await output);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
