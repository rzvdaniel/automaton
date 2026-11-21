using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed record ServerMaintenanceRunDto(
    Guid Id, string Kind, Guid? RunnerId, string? RequestedBy, DateTime StartedAtUtc, DateTime? FinishedAtUtc,
    string State, string? Message, int? UpgradedCount);

/// <summary>
/// The last maintenance runs of the server with who started them and how they ended. The host scripts only keep their
/// latest result, so <see cref="SyncAsync"/> copies each finished run into the database; it runs on a timer and before every read.
/// </summary>
public sealed class ServerMaintenanceHistory(ApplicationDbContext db, IConfiguration configuration, RunnerUpgradeService upgrades)
{
    public const int PageSize = 20;
    private const int Retained = 200;

    public async Task<IReadOnlyList<ServerMaintenanceRunDto>> ListAsync(DateTimeOffset now, CancellationToken token)
    {
        await SyncAsync(now, token);
        var rows = await db.ServerMaintenanceRuns.AsNoTracking().OrderByDescending(run => run.StartedAtUtc).Take(PageSize).ToListAsync(token);
        return rows.Select(run => new ServerMaintenanceRunDto(run.Id, run.Kind, run.RunnerId, run.RequestedBy,
            run.StartedAtUtc, run.FinishedAtUtc, run.State, run.Message, run.UpgradedCount)).ToList();
    }

    /// <summary>Records the Runner upgrade an administrator just started.</summary>
    public async Task RecordUpgradeStartedAsync(Guid runnerId, string? requestedBy, DateTimeOffset now, CancellationToken token)
    {
        db.ServerMaintenanceRuns.Add(new ServerMaintenanceRun
        {
            Id = Guid.NewGuid(), Kind = ServerMaintenanceKinds.RunnerUpgrade, RunnerId = runnerId,
            RequestedBy = requestedBy, StartedAtUtc = now.UtcDateTime, State = ServerMaintenanceStates.Running
        });
        await db.SaveChangesAsync(token);
    }

    public async Task SyncAsync(DateTimeOffset now, CancellationToken token)
    {
        await SyncOsUpdateAsync(now, token);
        await SyncRestartAsync(now, token);
        await SyncBackupAsync(token);
        await SyncUpgradesAsync(now, token);
        var extra = await db.ServerMaintenanceRuns.OrderByDescending(run => run.StartedAtUtc).Skip(Retained).ToListAsync(token);
        if (extra.Count > 0) db.ServerMaintenanceRuns.RemoveRange(extra);
        await db.SaveChangesAsync(token);
    }

    private async Task SyncOsUpdateAsync(DateTimeOffset now, CancellationToken token)
    {
        var status = await ServerOsUpdate.ReadAsync(configuration, now, token);
        if (status.State is not ("running" or "succeeded" or "failed" or "refused") || (status.RequestedAtUtc ?? status.FinishedAtUtc) is not { } started) return;
        var run = await UpsertAsync(ServerMaintenanceKinds.OsUpdate, null, started, token);
        run.RequestedBy = status.RequestedBy;
        run.State = status.State;
        run.FinishedAtUtc = status.State == "running" ? null : (status.FinishedAtUtc ?? now).UtcDateTime;
        run.UpgradedCount = status.UpgradedCount;
        run.Message = Trim(status.State == "succeeded" ? Describe(status) : status.Message);
    }

    private static string Describe(ServerOsUpdateStatus status)
        => status.UpgradedCount is { } count ? $"{count} packages upgraded{(status.RestartRequired == true ? "; restart required" : "")}" : status.Message ?? "Updated";

    private async Task SyncRestartAsync(DateTimeOffset now, CancellationToken token)
    {
        var status = await ServerRestart.ReadAsync(configuration, now, token);
        if (status.LastRestartedAtUtc is not { } restarted) return;
        var run = await UpsertAsync(ServerMaintenanceKinds.Restart, null, restarted, token);
        run.RequestedBy ??= status.RequestedBy;
        run.State = ServerMaintenanceStates.Succeeded;
        run.FinishedAtUtc = restarted.UtcDateTime;
        run.Message = "The server restarted";
    }

    private async Task SyncBackupAsync(CancellationToken token)
    {
        var status = await InstallationBackups.ReadAsync(configuration, token);
        if (status.LastAttemptAt is not { } attempt) return;
        var run = await UpsertAsync(ServerMaintenanceKinds.Backup, null, attempt, token);
        var succeeded = status.LastSuccessAt is { } success && success >= attempt && status.LastError is null;
        run.State = succeeded ? ServerMaintenanceStates.Succeeded : ServerMaintenanceStates.Failed;
        run.FinishedAtUtc = (succeeded ? status.LastSuccessAt!.Value : attempt).UtcDateTime;
        run.Message = Trim(succeeded ? "Backup completed" : status.LastError ?? "Backup failed");
    }

    /// <summary>Settles started Runner upgrades: the Runner reconnected on the new image, was rolled back, or never answered.</summary>
    private async Task SyncUpgradesAsync(DateTimeOffset now, CancellationToken token)
    {
        var open = await db.ServerMaintenanceRuns.Where(run => run.Kind == ServerMaintenanceKinds.RunnerUpgrade
            && run.State == ServerMaintenanceStates.Running && run.RunnerId != null).ToListAsync(token);
        foreach (var run in open)
        {
            try
            {
                var status = await upgrades.StatusAsync(run.RunnerId!.Value, token, trusted: true);
                if (status.State == RunnerUpgradeStates.Reconnected) Finish(run, ServerMaintenanceStates.Succeeded, "The Runner reconnected on the current image", now);
                else if (status.State == RunnerUpgradeStates.RolledBack) Finish(run, ServerMaintenanceStates.Failed, status.Message, now);
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or KeyNotFoundException)
            {
                // The Runner is gone or cannot answer; the stale rule below ends the run.
            }
            if (run.State == ServerMaintenanceStates.Running && now.UtcDateTime - run.StartedAtUtc > TimeSpan.FromHours(1))
                Finish(run, ServerMaintenanceStates.Failed, "The upgrade did not finish within an hour", now);
        }
    }

    private static void Finish(ServerMaintenanceRun run, string state, string message, DateTimeOffset now)
    {
        run.State = state;
        run.FinishedAtUtc = now.UtcDateTime;
        run.Message = Trim(message);
    }

    private async Task<ServerMaintenanceRun> UpsertAsync(string kind, Guid? runnerId, DateTimeOffset started, CancellationToken token)
    {
        var at = started.UtcDateTime;
        var run = await db.ServerMaintenanceRuns.FirstOrDefaultAsync(item => item.Kind == kind && item.RunnerId == runnerId && item.StartedAtUtc == at, token);
        if (run is null)
        {
            run = new ServerMaintenanceRun { Id = Guid.NewGuid(), Kind = kind, RunnerId = runnerId, StartedAtUtc = at };
            db.ServerMaintenanceRuns.Add(run);
        }
        return run;
    }

    private static string? Trim(string? text) => text is { Length: > 500 } ? text[..500] : text;
}
