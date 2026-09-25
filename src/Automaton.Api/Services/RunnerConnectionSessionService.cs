using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Tracks Runner connection session boundaries and emits their application events.
/// </summary>
public sealed class RunnerConnectionSessionService(
    ApplicationDbContext dbContext,
    IApplicationEventService applicationEventService,
    UserContextService userContextService)
{
    private readonly Serilog.ILogger logger = Serilog.Log.ForContext<RunnerConnectionSessionService>();

    private sealed record RunnerSessionPresenceState(
        Guid RunnerId,
        string? ConnectionId,
        DateTime? LastHeartbeatAtUtc);

    public async Task<int> CloseStaleConnectionSessionsAsync(CancellationToken cancellationToken)
    {
        var thresholdUtc = DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold;
        var staleRunners = await dbContext.Runners
            .Where(x => x.ConnectionId != null)
            .Where(x => x.LastHeartbeatAtUtc == null || x.LastHeartbeatAtUtc < thresholdUtc)
            .ToListAsync(cancellationToken);

        if (staleRunners.Count == 0)
            return 0;

        var staleRunnerIds = staleRunners.Select(x => x.Id).ToList();
        var openSessions = await dbContext.RunnerConnectionSessions
            .Where(x => staleRunnerIds.Contains(x.RunnerId) && x.DisconnectedAtUtc == null)
            .ToListAsync(cancellationToken);
        var sessionsByRunnerId = openSessions
            .GroupBy(x => x.RunnerId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var now = DateTime.UtcNow;
        var closedCount = 0;
        foreach (var runner in staleRunners)
        {
            if (string.IsNullOrWhiteSpace(runner.ConnectionId))
                continue;

            if (sessionsByRunnerId.TryGetValue(runner.Id, out var sessions))
            {
                foreach (var session in sessions.Where(x => string.Equals(x.ConnectionId, runner.ConnectionId, StringComparison.Ordinal)))
                {
                    session.DisconnectedAtUtc = now;
                    session.DisconnectReason = RunnerSessionDisconnectReasons.HeartbeatTimeout;
                    closedCount++;
                }
            }

            runner.ConnectionId = null;
            runner.LastDisconnectedAtUtc = now;
            runner.Status = RunnerConnectivityStatuses.Offline;
            dbContext.Entry(runner).State = EntityState.Modified;
        }

        if (closedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.Warning("Closed {SessionCount} stale runner connection session(s) after heartbeat timeout", closedCount);
            await WriteStaleDisconnectEventsAsync(staleRunners, cancellationToken);
        }

        return closedCount;
    }

    public async Task MarkDisconnectedAsync(Guid runnerId, string connectionId, CancellationToken cancellationToken)
    {
        var runner = await dbContext.Runners
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == runnerId, cancellationToken);
        if (runner == null || !string.Equals(runner.ConnectionId, connectionId, StringComparison.Ordinal))
            return;

        var disconnectedAtUtc = DateTime.UtcNow;
        var disconnected = await dbContext.Runners
            .Where(x => x.Id == runnerId && x.ConnectionId == connectionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ConnectionId, (string?)null)
                .SetProperty(x => x.LastDisconnectedAtUtc, disconnectedAtUtc)
                .SetProperty(x => x.Status, RunnerConnectivityStatuses.Offline),
                cancellationToken);
        if (disconnected != 1)
            return;

        var lostExecutions = await dbContext.AppModuleExecutions
            .Where(execution => execution.RunnerId == runner.Id
                && (execution.Status == AppModuleExecutionStatuses.Dispatching || execution.Status == AppModuleExecutionStatuses.Running))
            .ToListAsync(cancellationToken);
        foreach (var execution in lostExecutions)
        {
            execution.Status = AppModuleExecutionStatuses.Lost;
            execution.ErrorCode = AppModuleExecutionErrorCodes.RunnerUnavailable;
            execution.FailureSummary = "Runner disconnected before execution completion was confirmed.";
            execution.CompletedAtUtc = disconnectedAtUtc;
        }

        await CloseMatchingOpenSessionAsync(
            runner.Id,
            connectionId,
            disconnectedAtUtc,
            RunnerSessionDisconnectReasons.GracefulDisconnect,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        await WriteApplicationEventsAsync(
            runner.Id,
            ApplicationEventLevels.Warning,
            ApplicationEventCategories.Runtime,
            ApplicationEventTypes.RunnerDisconnected,
            "Runner disconnected.",
            new { runner.Name, runner.MachineName, Reason = RunnerSessionDisconnectReasons.GracefulDisconnect },
            userContextService.GetUserId(),
            cancellationToken);
    }

    public async Task CloseOpenSessionsAsync(
        Guid runnerId,
        DateTime disconnectedAtUtc,
        string disconnectReason,
        CancellationToken cancellationToken)
    {
        var openSessions = await dbContext.RunnerConnectionSessions
            .Where(x => x.RunnerId == runnerId && x.DisconnectedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in openSessions)
        {
            session.DisconnectedAtUtc = disconnectedAtUtc;
            session.DisconnectReason = disconnectReason;
        }
    }

    public async Task WriteApplicationEventsAsync(
        Guid runnerId,
        string level,
        string category,
        string eventType,
        string message,
        object? metadata,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var appIds = await dbContext.AppDeploymentTargets
            .AsNoTracking()
            .Where(x => x.ExecutionRunnerId == runnerId)
            .Select(x => x.WebAppId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var appId in appIds)
        {
            await applicationEventService.WriteAsync(new ApplicationEventWriteRequest
            {
                AppId = appId,
                Level = level,
                Category = category,
                EventType = eventType,
                Message = message,
                Metadata = metadata,
                ActorUserId = actorUserId
            }, cancellationToken);
        }
    }

    public async Task<Dictionary<Guid, RunnerSessionStats>> GetSessionStatsAsync(
        IEnumerable<Guid> runnerIds,
        CancellationToken cancellationToken)
    {
        var ids = runnerIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, RunnerSessionStats>();

        var runnerStates = await dbContext.Runners
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new RunnerSessionPresenceState(x.Id, x.ConnectionId, x.LastHeartbeatAtUtc))
            .ToDictionaryAsync(x => x.RunnerId, cancellationToken);

        var sessions = await dbContext.RunnerConnectionSessions
            .AsNoTracking()
            .Where(x => ids.Contains(x.RunnerId))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        return sessions
            .GroupBy(x => x.RunnerId)
            .ToDictionary(group => group.Key, group => CalculateSessionStats(group, runnerStates, now));
    }

    public async Task PopulateSessionStatsAsync(RunnerDetails runner, CancellationToken cancellationToken)
    {
        var stats = await GetSessionStatsAsync([runner.Id], cancellationToken);
        if (!stats.TryGetValue(runner.Id, out var value))
            return;

        runner.CurrentSessionConnectedAtUtc = value.CurrentSessionConnectedAtUtc;
        runner.ConnectionSessionCount = value.ConnectionSessionCount;
        runner.TotalConnectedDurationSeconds = value.TotalConnectedDurationSeconds;
        runner.CurrentSessionId = value.CurrentSessionId;
        runner.LastDisconnectReason = value.LastDisconnectReason;
    }

    private async Task CloseMatchingOpenSessionAsync(
        Guid runnerId,
        string connectionId,
        DateTime disconnectedAtUtc,
        string disconnectReason,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.RunnerConnectionSessions
            .Where(x => x.RunnerId == runnerId && x.ConnectionId == connectionId && x.DisconnectedAtUtc == null)
            .OrderByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (session == null)
            return;

        session.DisconnectedAtUtc = disconnectedAtUtc;
        session.DisconnectReason = disconnectReason;
    }

    private async Task WriteStaleDisconnectEventsAsync(
        IEnumerable<Runner> staleRunners,
        CancellationToken cancellationToken)
    {
        foreach (var runner in staleRunners)
        {
            await WriteApplicationEventsAsync(
                runner.Id,
                ApplicationEventLevels.Error,
                ApplicationEventCategories.Runtime,
                ApplicationEventTypes.RunnerDisconnected,
                "Runner disconnected after heartbeat timeout.",
                new { runner.Name, Reason = RunnerSessionDisconnectReasons.HeartbeatTimeout },
                userContextService.GetUserId(),
                cancellationToken);
        }
    }

    private static RunnerSessionStats CalculateSessionStats(
        IEnumerable<RunnerConnectionSession> sessions,
        IReadOnlyDictionary<Guid, RunnerSessionPresenceState> runnerStates,
        DateTime now)
    {
        var sessionList = sessions.ToList();
        runnerStates.TryGetValue(sessionList[0].RunnerId, out var runnerState);
        var totalSeconds = sessionList.Sum(session =>
        {
            var sessionEnd = session.DisconnectedAtUtc ?? GetOpenSessionEffectiveEnd(session, runnerState, now);
            var duration = sessionEnd - session.ConnectedAtUtc;
            return duration > TimeSpan.Zero ? Convert.ToInt64(duration.TotalSeconds) : 0L;
        });
        var currentSession = sessionList
            .Where(session => session.DisconnectedAtUtc == null)
            .OrderByDescending(session => session.ConnectedAtUtc)
            .FirstOrDefault();
        var currentSessionConnectedAtUtc =
            runnerState != null && RunnerPresenceEvaluator.IsOnline(runnerState.ConnectionId, runnerState.LastHeartbeatAtUtc, now)
                ? currentSession?.ConnectedAtUtc
                : null;

        return new RunnerSessionStats(
            totalSeconds,
            sessionList.Count,
            currentSessionConnectedAtUtc,
            currentSession?.Id,
            sessionList.Where(session => session.DisconnectedAtUtc.HasValue)
                .OrderByDescending(session => session.DisconnectedAtUtc)
                .Select(session => session.DisconnectReason)
                .FirstOrDefault());
    }

    private static DateTime GetOpenSessionEffectiveEnd(
        RunnerConnectionSession session,
        RunnerSessionPresenceState? runnerState,
        DateTime utcNow)
    {
        if (runnerState == null)
            return session.ConnectedAtUtc;

        return RunnerPresenceEvaluator.IsOnline(runnerState.ConnectionId, runnerState.LastHeartbeatAtUtc, utcNow)
            ? utcNow
            : runnerState.LastHeartbeatAtUtc ?? session.ConnectedAtUtc;
    }
}

public sealed record RunnerSessionStats(
    long TotalConnectedDurationSeconds,
    int ConnectionSessionCount,
    DateTime? CurrentSessionConnectedAtUtc,
    Guid? CurrentSessionId,
    string? LastDisconnectReason);
