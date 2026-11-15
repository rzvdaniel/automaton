using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Measures the disk each app uses on this Runner's machine: its files and logs under the Runner workspace, its data
/// directory, and the Docker figures for its containers and images. The walk is not free, so a result is reused for a while.
/// Only the host health loop calls it, one call at a time, so the cache needs no lock.
/// </summary>
public sealed class AppDiskMeasurer(RunnerWorkspaceService workspace)
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(14);
    private DateTime measuredAt = DateTime.MinValue;
    private IReadOnlyList<AppDiskUsage>? cached;

    public IReadOnlyList<AppDiskUsage> Measure(Dictionary<Guid, (long Container, long Image)>? docker)
    {
        if (cached is not null && DateTime.UtcNow - measuredAt < MaximumAge) return Merge(cached, docker);
        var ids = new HashSet<Guid>(ListApps(workspace.AppsRoot).Concat(ListApps(workspace.DataRoot)));
        cached = ids.Select(MeasureFiles).ToArray();
        measuredAt = DateTime.UtcNow;
        return Merge(cached, docker);
    }

    private static IReadOnlyList<AppDiskUsage> Merge(IReadOnlyList<AppDiskUsage> files, Dictionary<Guid, (long Container, long Image)>? docker)
    {
        var result = files.Select(item => docker is not null && docker.TryGetValue(item.AppId, out var usage)
            ? item with { ContainerBytes = usage.Container, ImageBytes = usage.Image }
            : item with { ContainerBytes = docker is null ? null : 0, ImageBytes = docker is null ? null : 0 }).ToList();
        if (docker is not null)
            foreach (var (appId, usage) in docker.Where(pair => files.All(item => item.AppId != pair.Key)))
                result.Add(new(appId, null, null, null, usage.Container, usage.Image));
        return result;
    }

    private AppDiskUsage MeasureFiles(Guid appId)
    {
        var appRoot = Path.Combine(workspace.AppsRoot, appId.ToString());
        var logs = 0L;
        var targets = Path.Combine(appRoot, "targets");
        if (Directory.Exists(targets))
            foreach (var target in SafeDirectories(targets)) logs += DirectorySize(Path.Combine(target, "logs"));
        var all = DirectorySize(appRoot);
        return new(appId, Math.Max(0, all - logs), DirectorySize(Path.Combine(workspace.DataRoot, appId.ToString())), logs, null, null);
    }

    private static IEnumerable<Guid> ListApps(string root) =>
        Directory.Exists(root)
            ? SafeDirectories(root).Select(path => Guid.TryParse(Path.GetFileName(path), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)
            : [];

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Bytes in a directory tree, counting what this process may read (files an app owns privately are skipped).</summary>
    internal static long DirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                try { total += file.Length; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return total;
    }
}
