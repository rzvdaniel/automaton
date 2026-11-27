using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Gives each app deployment target host ports from the reserved runtime range and remembers them on disk, so Docker
/// never chooses an app's port and no other app can be given one that an app still has on record (#1791).
/// An app owns up to two ports: a new candidate container is published on the one its running container does not
/// use, so a blue-green switch never needs both containers on one port. Both stay the app's until it is released.
/// </summary>
internal sealed class AppHostPortLedger
{
    private const int SlotsPerApp = 2;
    private static readonly object Gate = new();
    private readonly string path;
    private readonly int start;
    private readonly int end;
    private readonly ISet<int> excluded;

    public AppHostPortLedger(string path, int start, int end, IEnumerable<int>? excluded = null)
    {
        this.path = path;
        this.start = start;
        this.end = end;
        this.excluded = new HashSet<int>(excluded ?? []);
    }

    public static AppHostPortLedger ForWorkspace(RunnerWorkspaceService workspace)
        => new(Path.Combine(workspace.WorkspaceRoot, "host-ports.json"),
            ReadPort("AUTOMATON_RUNTIME_PORT_START", 18000), ReadPort("AUTOMATON_RUNTIME_PORT_END", 18999),
            [RunnerPlatformPorts.GetRuntimeGatewayPort()]);

    /// <summary>The app's own port that none of its containers use, or a new one while it owns fewer than two.</summary>
    public int Reserve(Guid appId, Guid targetId, ISet<int> usedByApp, Func<int, bool> canBind)
    {
        lock (Gate)
        {
            var all = Read();
            var key = Key(appId, targetId);
            var owned = all.TryGetValue(key, out var existing) ? existing : [];
            var free = owned.FirstOrDefault(port => !usedByApp.Contains(port));
            if (free != 0) return free;
            if (owned.Count >= SlotsPerApp)
                throw new InvalidOperationException("Both host ports of the app are in use by its containers.");
            var taken = all.Values.SelectMany(ports => ports).ToHashSet();
            for (var port = start; port <= end; port++)
            {
                if (excluded.Contains(port) || taken.Contains(port) || usedByApp.Contains(port) || !canBind(port)) continue;
                owned.Add(port);
                all[key] = owned;
                Write(all);
                return port;
            }
            throw new InvalidOperationException("No managed runtime ports are available.");
        }
    }

    public void Release(Guid appId, Guid targetId)
    {
        lock (Gate)
        {
            var all = Read();
            if (all.Remove(Key(appId, targetId))) Write(all);
        }
    }

    /// <summary>Host ports published by container listings in <c>docker ps --format {{.Ports}}</c> form.</summary>
    public static ISet<int> ParsePublishedPorts(string dockerPsPorts)
        => Regex.Matches(dockerPsPorts, @":(\d+)->", RegexOptions.CultureInvariant)
            .Select(match => int.Parse(match.Groups[1].Value)).ToHashSet();

    private static string Key(Guid appId, Guid targetId) => $"{appId:N}-{targetId:N}";

    private Dictionary<string, List<int>> Read()
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<int>>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException) { throw new InvalidOperationException("The host port record is unreadable; not guessing ports."); }
    }

    private void Write(Dictionary<string, List<int>> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(all));
        File.Move(temp, path, overwrite: true);
    }

    private static int ReadPort(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value is >= 1 and <= 65535 ? value : fallback;
}
