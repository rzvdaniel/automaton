using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Runner.Console.Services;

/// <summary>Pure parsers for the host figures the Machine Management Runner reports.</summary>
internal static partial class HostHealthParsers
{
    public static long? ParseUptimeSeconds(string? uptime)
        => double.TryParse(uptime?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 ? (long)seconds : null;

    public static (double Load1, double Load5, double Load15)? ParseLoad(string? loadavg)
    {
        var parts = loadavg?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: >= 3 }) return null;
        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var one)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var five)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var fifteen)
            ? (one, five, fifteen) : null;
    }

    public static (long Total, long Available)? ParseMemory(string? meminfo)
    {
        long? total = null, available = null;
        foreach (var line in (meminfo ?? "").Split('\n'))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) total = Kilobytes(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) available = Kilobytes(line);
        }
        return total is > 0 && available is >= 0 && available <= total ? (total.Value, available.Value) : null;
    }

    private static long? Kilobytes(string line)
    {
        var digits = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var kb) ? kb * 1024 : null;
    }

    /// <summary>Total and idle jiffies of the aggregate "cpu" line, plus the number of cpuN lines.</summary>
    public static (long Total, long Idle, int Cpus)? ParseCpuStat(string? stat)
    {
        long total = 0, idle = 0;
        var cpus = 0;
        var found = false;
        foreach (var line in (stat ?? "").Split('\n'))
        {
            if (line.StartsWith("cpu ", StringComparison.Ordinal))
            {
                var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                    .Select(item => long.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0).ToArray();
                if (values.Length < 5) return null;
                // user nice system idle iowait irq softirq steal: guest time is already inside user.
                total = values.Take(8).Sum();
                idle = values[3] + values[4];
                found = true;
            }
            else if (line.StartsWith("cpu", StringComparison.Ordinal) && char.IsDigit(line.ElementAtOrDefault(3))) cpus++;
        }
        return found ? (total, idle, cpus) : null;
    }

    public static double? CpuPercent((long Total, long Idle, int Cpus) first, (long Total, long Idle, int Cpus) second)
    {
        var total = second.Total - first.Total;
        if (total <= 0) return null;
        return Math.Round(Math.Clamp(100.0 * (total - (second.Idle - first.Idle)) / total, 0, 100), 1);
    }

    public static (long Images, long Containers, long Volumes, long BuildCache)? ParseDockerDiskUsage(string? json)
    {
        try
        {
            using var document = JsonDocument.Parse(json ?? "");
            var root = document.RootElement;
            var images = root.TryGetProperty("LayersSize", out var layers) && layers.TryGetInt64(out var size) ? size : 0;
            return (images,
                Sum(root, "Containers", item => Number(item, "SizeRw")),
                Sum(root, "Volumes", item => item.TryGetProperty("UsageData", out var usage) ? Number(usage, "Size") : 0),
                Sum(root, "BuildCache", item => item.TryGetProperty("Shared", out var shared) && shared.ValueKind == JsonValueKind.True
                    ? 0 : Number(item, "Size")));
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static long Number(JsonElement item, string name)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private static long Sum(JsonElement root, string name, Func<JsonElement, long> size)
        => root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Sum(item => Math.Max(0, size(item))) : 0;

    /// <summary>Sections printed by the read-only helper container: [os], [updates], [reboot].</summary>
    public static Dictionary<string, string> SplitSections(string? output)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        foreach (var line in (output ?? "").Replace("\r", "").Split('\n'))
        {
            if (line.Length > 2 && line[0] == '[' && line[^1] == ']') { current = line[1..^1]; sections[current] = ""; }
            else if (current is not null) sections[current] += line + "\n";
        }
        return sections;
    }

    public static (string? Name, string? Version) ParseOsRelease(string? osRelease)
    {
        string? pretty = null, name = null, version = null;
        foreach (var line in (osRelease ?? "").Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator < 1) continue;
            var value = line[(separator + 1)..].Trim().Trim('"');
            switch (line[..separator])
            {
                case "PRETTY_NAME": pretty = value; break;
                case "NAME": name = value; break;
                case "VERSION_ID": version = value; break;
            }
        }
        return (pretty ?? name, version);
    }

    /// <summary>Ubuntu's update-notifier summary; null counts when the file is missing or unreadable.</summary>
    public static (int? Pending, int? Security) ParseUpdatesAvailable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var pending = PendingPattern().Match(text);
        if (!pending.Success) return (null, null);
        var security = SecurityPattern().Match(text);
        return (int.Parse(pending.Groups[1].Value, CultureInfo.InvariantCulture),
            security.Success ? int.Parse(security.Groups[1].Value, CultureInfo.InvariantCulture) : 0);
    }

    [GeneratedRegex(@"(\d+) updates? can be applied", RegexOptions.IgnoreCase)]
    private static partial Regex PendingPattern();

    [GeneratedRegex(@"(\d+) of these updates? (?:is|are) (?:a )?standard security updates?", RegexOptions.IgnoreCase)]
    private static partial Regex SecurityPattern();
}
