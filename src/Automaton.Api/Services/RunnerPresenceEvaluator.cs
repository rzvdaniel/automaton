using Automaton.Core.Enums;

namespace Automaton.Api.Services;

public static class RunnerPresenceEvaluator
{
    public static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(10);

    public static bool IsOnline(string? connectionId, DateTime? lastHeartbeatAtUtc, DateTime utcNow)
    {
        return !string.IsNullOrWhiteSpace(connectionId) && HasRecentHeartbeat(lastHeartbeatAtUtc, utcNow);
    }

    public static bool HasRecentHeartbeat(DateTime? lastHeartbeatAtUtc, DateTime utcNow)
        => lastHeartbeatAtUtc.HasValue && utcNow - lastHeartbeatAtUtc.Value <= OfflineThreshold;

    public static RunnerStatus DeriveStatus(DateTime? lastHeartbeatAtUtc, DateTime utcNow)
    {
        if (!lastHeartbeatAtUtc.HasValue)
        {
            return RunnerStatus.None;
        }

        return utcNow - lastHeartbeatAtUtc.Value <= OfflineThreshold
            ? RunnerStatus.Online
            : RunnerStatus.Offline;
    }

    public static RunnerStatus DeriveStatus(string? connectionId, DateTime? lastHeartbeatAtUtc, DateTime utcNow)
    {
        if (!lastHeartbeatAtUtc.HasValue)
        {
            return RunnerStatus.None;
        }

        return IsOnline(connectionId, lastHeartbeatAtUtc, utcNow)
            ? RunnerStatus.Online
            : RunnerStatus.Offline;
    }
}
