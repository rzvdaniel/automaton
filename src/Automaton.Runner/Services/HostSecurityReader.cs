using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Runs the installer's host security check (<c>host_security.py</c>, baked into the Runner image) read-only in a short-lived helper
/// container: the host network namespace (to see listening ports), no capabilities, and /etc/ssh, /etc/passwd, /etc/ufw and home
/// directories mounted read-only. It changes nothing; fixes are made by the installer or by the user.
/// </summary>
internal static class HostSecurityReader
{
    private const int MaxFindings = 60;

    public static async Task<HostSecurityStatus?> ReadAsync(string? image, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        var args = new List<string>
        {
            "run", "--rm", "--network", "host", "--read-only", "--cap-drop", "ALL", "--user", "0:0",
            "--security-opt", "no-new-privileges", "--pull", "never", "--entrypoint", "python"
        };
        // Each path is optional on the host (no ufw, no /home): docker refuses a missing bind source, so only existing ones are mounted.
        foreach (var path in new[] { "/etc/ssh", "/etc/passwd", "/etc/ufw", "/root", "/home", "/run/firewalld.pid" })
            if (File.Exists(path) || Directory.Exists(path))
                args.AddRange(["--mount", $"type=bind,source={path},target=/host{path},readonly"]);
        args.AddRange([image, "/app/host_security.py", "--json", "--root", "/host"]);
        var result = await HostHealthCollector.RunAsync("docker", args, token);
        return result?.ExitCode == 0 ? Parse(result.Output) : null;
    }

    internal static HostSecurityStatus? Parse(string? output)
    {
        try
        {
            using var document = JsonDocument.Parse(output ?? "");
            var root = document.RootElement;
            var findings = root.GetProperty("findings").EnumerateArray().Select(item => new HostSecurityFinding(
                item.GetProperty("id").GetString() ?? "", item.GetProperty("title").GetString() ?? "", item.GetProperty("detail").GetString() ?? ""))
                .Where(item => item.Id.Length is > 0 and <= 60 && item.Title.Length is > 0 and <= 200).Take(MaxFindings)
                .Select(item => item with { Detail = item.Detail.Length > 500 ? item.Detail[..500] : item.Detail }).ToArray();
            return new(root.TryGetProperty("firewall", out var firewall) && firewall.ValueKind == JsonValueKind.String ? firewall.GetString() : null, findings);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
