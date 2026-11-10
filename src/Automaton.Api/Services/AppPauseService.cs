using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <param name="Paused">Whether the app is paused after the call.</param>
/// <param name="SchedulesChanged">Schedules turned off by a pause, or turned on again by a resume.</param>
/// <param name="Warnings">Anything that could not be done, such as a schedule that cannot be enabled again.</param>
public sealed record AppPauseState(
    Guid AppId, bool Paused, DateTime? PausedAt, int SchedulesChanged, IReadOnlyList<string> Warnings);

/// <summary>
/// Pauses an app without deleting it: its runtime stops on the Runner, its schedules are switched off and its address
/// answers "paused". Resuming starts the runtime again and turns on exactly the schedules that were on before.
/// The permission rule is the one for deploying the app (its owner and project members who may deploy), plus administrators.
/// </summary>
public sealed class AppPauseService(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    ActionScheduleService schedules,
    IAppRuntimeCommandQueue operations,
    UserContextService user,
    IAuditEventService auditEvents,
    ILogger<AppPauseService> logger)
{
    public const string PauseCommandKind = "pause-runtime";
    public const string ResumeCommandKind = "resume-runtime";

    public Task<AppPauseState> PauseAsync(Guid appId, bool administrator, CancellationToken ct) => ChangeAsync(appId, administrator, pause: true, ct);

    public Task<AppPauseState> ResumeAsync(Guid appId, bool administrator, CancellationToken ct) => ChangeAsync(appId, administrator, pause: false, ct);

    private async Task<AppPauseState> ChangeAsync(Guid appId, bool administrator, bool pause, CancellationToken ct)
    {
        var userId = user.GetUserId();
        var app = await db.WebApps.FirstOrDefaultAsync(item => item.Id == appId && item.DeletionClaimId == null, ct)
            ?? throw new KeyNotFoundException("App not found");
        if (!administrator)
        {
            // An app the person cannot see is "not found", like for deploy; one they can see but not deploy is refused.
            if (!await authorization.CanAccessAppAsync(appId, userId, ct)) throw new KeyNotFoundException("App not found");
            if (!await authorization.CanUpdateAppAsync(appId, userId, ct))
                throw new UnauthorizedAccessException("You do not have permission to pause or resume this app.");
        }

        var warnings = new List<string>();
        var stateChanged = app.IsPaused != pause;
        if (stateChanged)
        {
            app.IsPaused = pause;
            app.PausedAt = pause ? DateTime.UtcNow : null;
            app.Updated = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        // Schedules are handled on every call, so repeating a call finishes anything an earlier call left undone.
        var changed = pause ? await PauseSchedulesAsync(appId, ct) : await ResumeSchedulesAsync(appId, userId, warnings, ct);
        if (stateChanged)
        {
            await auditEvents.WriteAsync(new AuditEventWriteRequest
            {
                Action = AuditActions.Changed,
                EntityType = AuditEntityTypes.App,
                EntityId = appId,
                EventType = pause ? AuditEventTypes.AppPaused : AuditEventTypes.AppResumed,
                Message = pause ? "App paused" : "App resumed",
                Outcome = AuditOutcomes.Success,
                ActorUserId = userId,
                Metadata = new { AppId = appId, SchedulesChanged = changed }
            });
        }
        // Repeating the call also repeats the Runner command, so a Runner that missed it catches up.
        await QueueRuntimeCommandsAsync(appId, userId, pause, warnings, ct);
        return new AppPauseState(appId, app.IsPaused, app.PausedAt, changed, warnings);
    }

    private async Task<int> PauseSchedulesAsync(Guid appId, CancellationToken ct)
    {
        var enabled = await db.ActionSchedules.Where(item => item.ApplicationId == appId && item.Enabled).ToListAsync(ct);
        var ids = enabled.Select(item => item.Id).ToList();
        var waiting = await db.ActionScheduleOccurrences
            .Where(x => ids.Contains(x.ScheduleId)
                && (x.Status == ActionScheduleOccurrenceStatuses.Queued || x.Status == ActionScheduleOccurrenceStatuses.Published))
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var occurrence in waiting)
        {
            occurrence.Status = ActionScheduleOccurrenceStatuses.Skipped;
            occurrence.FailureSummary = "App paused before dispatch.";
            occurrence.UpdatedAtUtc = now;
        }
        foreach (var schedule in enabled)
        {
            schedule.Enabled = false;
            schedule.NextRunAtUtc = null;
            schedule.PausedByApp = true;
            schedule.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        return enabled.Count;
    }

    private async Task<int> ResumeSchedulesAsync(Guid appId, Guid userId, List<string> warnings, CancellationToken ct)
    {
        var paused = await db.ActionSchedules.Where(item => item.ApplicationId == appId && item.PausedByApp)
            .Select(item => new { item.Id, item.Name }).ToListAsync(ct);
        var count = 0;
        foreach (var item in paused)
        {
            try
            {
                var schedule = await schedules.SetEnabledAsync(item.Id, true, userId, administrator: true, ct);
                schedule.PausedByApp = false;
                await db.SaveChangesAsync(ct);
                count++;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // It stays off and marked, so calling resume again retries it; the rest of the resume carries on.
                foreach (var entry in db.ChangeTracker.Entries<ActionSchedule>().Where(entry => entry.Entity.Id == item.Id).ToList())
                    entry.State = EntityState.Detached;
                warnings.Add($"Schedule '{item.Name}' could not be turned on again: {error.Message}");
            }
        }
        return count;
    }

    /// <summary>
    /// A deploy starts the new runtime; when the app is paused it is stopped again and stays paused.
    /// Returns the note to add to the deploy result, or null when the app is not paused.
    /// </summary>
    public async Task<string?> KeepPausedAfterDeployAsync(Guid appId, Guid targetId, Guid runnerId, Guid userId, CancellationToken ct)
    {
        try { return await KeepPausedCoreAsync(appId, targetId, runnerId, userId, ct); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Could not keep paused app {AppId} stopped after a deploy.", appId);
            return "The app is paused, but it could not be checked after the deploy, so the new version may be running. Pause the app again to retry.";
        }
    }

    private async Task<string?> KeepPausedCoreAsync(Guid appId, Guid targetId, Guid runnerId, Guid userId, CancellationToken ct)
    {
        if (!await db.WebApps.AnyAsync(app => app.Id == appId && app.IsPaused, ct)) return null;
        try { await QueueCommandAsync(appId, targetId, runnerId, userId, pause: true, ct); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Could not queue a runtime pause after deploying paused app {AppId}.", appId);
            return "The app is paused, but the Runner could not be told to stop the new version, so it may be running. Pause the app again to retry.";
        }
        return "The app is paused, so the new version is deployed but stays stopped. Resume it with 'automaton resume' (or deploy with --resume).";
    }

    private Task QueueCommandAsync(Guid appId, Guid targetId, Guid runnerId, Guid userId, bool pause, CancellationToken ct)
    {
        AppActionCommand command = pause
            ? AppActionCommand.Create(PauseCommandKind, new PauseAppRuntimeRequest(appId, targetId))
            : AppActionCommand.Create(ResumeCommandKind, new ResumeAppRuntimeRequest(appId, targetId));
        return operations.QueueAsync(runnerId, userId, command, ct);
    }

    private async Task QueueRuntimeCommandsAsync(Guid appId, Guid userId, bool pause, List<string> warnings, CancellationToken ct)
    {
        foreach (var target in await AppRuntimeRestarter.PausableTargets(db, appId).ToListAsync(ct))
        {
            try
            {
                // A resume waits for the Runner, so a start that fails is reported instead of answered with 200.
                if (pause) await QueueCommandAsync(appId, target.Id, target.RunnerId, userId, pause, ct);
                else await operations.RunAsync(target.RunnerId, userId,
                    AppActionCommand.Create(ResumeCommandKind, new ResumeAppRuntimeRequest(appId, target.Id)), ct);
            }
            catch (RuntimeCommandFailedException)
            {
                throw;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Could not queue a runtime {Command} for app {AppId} target {TargetId}.",
                    pause ? "pause" : "resume", appId, target.Id);
                if (!pause) throw new RuntimeCommandFailedException("The Runner could not be told to start the app, so it is still stopped. Try again when the Runner is online.");
                warnings.Add("The Runner could not be told to stop the app. Its address still answers as running.");
            }
        }
    }
}

/// <summary>The Runner could not carry out a start or restart; the message is safe to show to the person who asked.</summary>
public sealed class RuntimeCommandFailedException(string message) : Exception(message);

/// <summary>Sends a command to a Runner to run later; the Runner executes it when it is online.</summary>
public interface IAppRuntimeCommandQueue
{
    /// <summary>Sends the command and waits for the Runner's answer; throws <see cref="RuntimeCommandFailedException"/> when it fails.</summary>
    Task<RunnerExecutionResult> RunAsync(Guid runnerId, Guid actorUserId, AppActionCommand command, CancellationToken cancellationToken);

    Task QueueAsync(Guid runnerId, Guid actorUserId, AppActionCommand command, CancellationToken cancellationToken);
}

/// <summary>Queues Runner commands as durable Runner execution operations.</summary>
public sealed class RunnerExecutionAppCommandQueue(RunnerExecutionOperationService operations) : IAppRuntimeCommandQueue
{
    public Task QueueAsync(Guid runnerId, Guid actorUserId, AppActionCommand command, CancellationToken cancellationToken)
        => operations.QueueAsync(runnerId, actorUserId, new(null, null, command), cancellationToken);

    public Task<RunnerExecutionResult> RunAsync(Guid runnerId, Guid actorUserId, AppActionCommand command, CancellationToken cancellationToken)
        => operations.QueueAndWaitAsync(runnerId, actorUserId, new(null, null, command), RuntimeStartTimeout, cancellationToken);

    /// <summary>Longer than an app's start-up wait (60 seconds unless the app asks for more), so a slow start still reports its own error.</summary>
    private static readonly TimeSpan RuntimeStartTimeout = TimeSpan.FromSeconds(150);
}
