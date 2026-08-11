using System.Text.Json;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Operator;

public sealed class FileOperatorTelemetry : IOperatorTelemetry
{
    private const int MaximumMessageLength = 512;
    private const int MaximumMetadataEntries = 20;
    private const int MaximumMetadataValueLength = 256;
    private readonly object gate = new();
    private readonly Dictionary<string, OperatorEvent> latest = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private readonly string eventsPath;
    private readonly int maximumBytes;
    private readonly IClock clock;
    private long sequence = DateTimeOffset.UtcNow.UtcTicks;
    private string daemonState = OperatorDaemonStates.Starting;
    private string? lastError;

    public FileOperatorTelemetry(OrchestratorOptions options, IClock clock)
    {
        this.clock = clock;
        maximumBytes = options.Observability.MaximumEventFileBytes;
        RootPath = Path.GetFullPath(Path.Combine(options.Workspace.Root, ".automaton-agent-orchestrator", "operator"));
        Directory.CreateDirectory(RootPath);
        eventsPath = Path.Combine(RootPath, "events.jsonl");
        StatusPath = Path.Combine(RootPath, "status.json");
    }

    public string RootPath { get; }
    public string StatusPath { get; }

    public void Record(string eventType, string severity, string? workItemId = null, string? stage = null, string? role = null, string? message = null, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var entry = new OperatorEvent(Interlocked.Increment(ref sequence), clock.UtcNow, Bound(eventType, 100) ?? "unknown", NormalizeSeverity(severity), Bound(workItemId, 200), Bound(stage, 100), Bound(role, 100), Bound(message, MaximumMessageLength), Sanitize(metadata));
        lock (gate)
        {
            if (entry.WorkItemId is not null) latest[entry.WorkItemId] = entry;
            try
            {
                RotateIfNeeded();
                File.AppendAllText(eventsPath, JsonSerializer.Serialize(entry, json) + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                daemonState = OperatorDaemonStates.Degraded;
                lastError = "operator event persistence failed";
            }
        }
    }

    public OperatorEvent? LatestFor(string workItemId) { lock (gate) return latest.GetValueOrDefault(workItemId); }
    public void SetDaemonState(string state, string? error = null) { lock (gate) { daemonState = state; lastError = Bound(error, MaximumMessageLength); } }
    public (string State, string? LastError) DaemonState() { lock (gate) return (daemonState, lastError); }

    public void WriteSnapshot(OperatorStatusSnapshot snapshot)
    {
        var temporary = StatusPath + ".tmp";
        lock (gate)
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, json));
            File.Move(temporary, StatusPath, true);
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(eventsPath) || new FileInfo(eventsPath).Length < maximumBytes) return;
        var previous = eventsPath + ".1";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(eventsPath, previous);
    }

    private static IReadOnlyDictionary<string, string>? Sanitize(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in metadata.Take(MaximumMetadataEntries))
        {
            var key = Bound(pair.Key, 100) ?? "unknown";
            result[key] = IsSensitive(key) ? "[REDACTED]" : Bound(pair.Value, MaximumMetadataValueLength) ?? string.Empty;
        }
        return result;
    }

    private static bool IsSensitive(string key) => key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("secret", StringComparison.OrdinalIgnoreCase) || key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("authorization", StringComparison.OrdinalIgnoreCase) || key.Contains("connection", StringComparison.OrdinalIgnoreCase) || key.Contains("key", StringComparison.OrdinalIgnoreCase);
    private static string NormalizeSeverity(string value) => value is OperatorEventSeverities.Debug or OperatorEventSeverities.Information or OperatorEventSeverities.Warning or OperatorEventSeverities.Error ? value : OperatorEventSeverities.Information;
    private static string? Bound(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(character => !char.IsControl(character) || character is '\n' or '\t').Take(maximum).ToArray()).Trim();
}
