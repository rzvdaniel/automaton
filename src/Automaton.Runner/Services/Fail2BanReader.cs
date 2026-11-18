using System.Net;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Reads fail2ban on the host through its database, never its socket: a short-lived helper container with no network and
/// no capabilities mounts the database and pid file read-only and prints JSON. Nothing here is exposed to apps.
/// </summary>
internal static class Fail2BanReader
{
    private const string Database = "/var/lib/fail2ban/fail2ban.sqlite3";
    private const string PidFile = "/run/fail2ban/fail2ban.pid";
    private const int MaxBansPerJail = 200;

    // Runs inside the helper with the Runner image's python. Prints one JSON object; {"error": true} when the database cannot be read.
    private const string Script = """
        import json, os, sqlite3, time
        now = time.time()
        out = {"running": os.path.exists("/host/fail2ban.pid"), "jails": []}
        try:
            if os.path.exists("/host/fail2ban.sqlite3"):
                db = sqlite3.connect("file:/host/fail2ban.sqlite3?mode=ro", uri=True, timeout=10)
                jails = {n: {"name": n, "enabled": bool(e), "lastHour": 0, "last24Hours": 0, "bans": [], "seen": set()}
                         for n, e in db.execute("select name, enabled from jails")}
                for jail, ip, at, length in db.execute("select jail, ip, timeofban, bantime from bans order by timeofban desc"):
                    item = jails.get(jail)
                    if item is None: continue
                    if at >= now - 86400: item["last24Hours"] += 1
                    if at >= now - 3600: item["lastHour"] += 1
                    if (length < 0 or at + length > now) and ip not in item["seen"] and len(item["bans"]) < 200:
                        item["seen"].add(ip)
                        item["bans"].append({"ip": ip, "at": at, "until": None if length < 0 else at + length})
                for item in jails.values(): del item["seen"]
                out["jails"] = list(jails.values())
        except Exception:
            out = {"error": True}
        print(json.dumps(out))
        """;

    public static async Task<Fail2BanStatus?> ReadAsync(string? image, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        (string Source, string Target)[] mounts = [(Database, "/host/fail2ban.sqlite3"), (PidFile, "/host/fail2ban.pid")];
        // Docker refuses a bind mount whose source is missing, so try with fewer mounts; none means fail2ban is absent.
        foreach (var attempt in new[] { mounts, mounts[..1], mounts[1..], [] })
        {
            var args = new List<string>
            {
                "run", "--rm", "--network", "none", "--read-only", "--cap-drop", "ALL", "--user", "0:0",
                "--security-opt", "no-new-privileges", "--pull", "never", "--entrypoint", "python"
            };
            foreach (var (source, target) in attempt)
                args.AddRange(["--mount", $"type=bind,source={source},target={target},readonly"]);
            args.AddRange([image, "-c", Script]);
            var result = await HostHealthCollector.RunAsync("docker", args, token);
            if (result?.ExitCode == 0) return Parse(result.Output, DateTime.UtcNow);
        }
        return null;
    }

    internal static Fail2BanStatus? Parse(string? output, DateTime nowUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(output ?? "");
            var root = document.RootElement;
            if (root.TryGetProperty("error", out _)) return null; // the database exists but could not be read: unknown, never "no bans"
            var jails = root.GetProperty("jails").EnumerateArray().Select(jail => new Fail2BanJail(
                jail.GetProperty("name").GetString() ?? "", jail.GetProperty("enabled").GetBoolean(),
                jail.GetProperty("lastHour").GetInt32(), jail.GetProperty("last24Hours").GetInt32(),
                jail.GetProperty("bans").EnumerateArray().Take(MaxBansPerJail)
                    .Select(ban => new Fail2BanBan(ban.GetProperty("ip").GetString() ?? "", FromUnix(ban.GetProperty("at").GetDouble()),
                        ban.GetProperty("until") is { ValueKind: JsonValueKind.Number } until ? FromUnix(until.GetDouble()) : null))
                    .Where(ban => IPAddress.TryParse(ban.Ip, out _)).ToArray()))
                .Where(jail => jail.Name.Length is > 0 and <= 100).Take(100).ToArray();
            return new(root.GetProperty("running").GetBoolean(), jails);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static DateTime FromUnix(double seconds)
        => DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
}
