using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Runs the installer's host security check (<c>host_security.py</c>, baked into the Runner image) read-only in a short-lived helper
/// container: the host network namespace (to see listening ports), one capability (DAC_READ_SEARCH, to read files) and the host's / mounted read-only. It changes nothing; fixes are made by the installer or by the user.
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
        // The Runner cannot see which host paths exist (its own /etc/ssh is not the host's), so the host's / is mounted read-only
        // like the other host helpers. DAC_READ_SEARCH only lets root read other users' authorized_keys and ufw's rules; nothing is writable.
        args.AddRange(["--cap-add", "DAC_READ_SEARCH", "--mount", "type=bind,source=/,target=/host,readonly"]);
        args.AddRange([image, "/app/host_security.py", "--json", "--root", "/host"]);
        // Ports Docker publishes skip ufw; the helper has no Docker socket, so the Runner names them.
        var published = await HostHealthCollector.RunAsync("docker", ["ps", "--format", "{{.Ports}}"], token);
        if (published?.ExitCode == 0)
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(published.Output, @":(\d{1,5})->"))
                args.AddRange(["--docker-port", match.Groups[1].Value]);
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
