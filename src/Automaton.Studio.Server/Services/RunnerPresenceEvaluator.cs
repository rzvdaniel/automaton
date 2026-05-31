using Automaton.Core.Enums;

namespace Automaton.Studio.Server.Services;

public static class RunnerPresenceEvaluator
{
    public static readonly TimeSpan OfflineThreshold = TimeSpan.FromMinutes(10);

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
}