using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services.Interfaces;
using System.Text.Json;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.ServerCleanup;

public sealed record RunnerMaintenancePermissionDto(Guid RunnerId, string Name, bool Approved);

/// <summary>Sends one named command to a Runner and waits for its answer.</summary>
public interface IRunnerMaintenanceCommands
{
    Task<JsonElement> SendAsync(Guid runnerId, Guid actor, AppActionCommand command, TimeSpan timeout, CancellationToken token);
}

public sealed class RunnerMaintenanceCommands(RunnerExecutionOperationService operations) : IRunnerMaintenanceCommands
{
    public async Task<JsonElement> SendAsync(Guid runnerId, Guid actor, AppActionCommand command, TimeSpan timeout, CancellationToken token)
    {
        var result = await operations.QueueAndWaitAsync(runnerId, actor, new RunnerExecutionRequest(command), timeout, token);
        return result.ActionResult ?? throw new InvalidDataException("The Runner answered with nothing.");
    }
}

public sealed record RunnerAppDto(Guid AppId, Guid TargetId, string Name, string Target, string Status);

public sealed record RunnerAppRestartDto(Guid AppId, Guid TargetId, bool Restarted);

/// <summary>
/// Named maintenance operations an app may run on one chosen machine. The app needs the <c>runner:maintain</c> permission
/// approved for the app and, for each machine, a per-Runner approval; every request is audit-logged. The operations are
/// fixed commands the Runner knows; an app never sends a shell command.
/// </summary>
public sealed class RunnerMaintenanceService(ApplicationDbContext db, ServerCleanupService cleanup, IAuditEventService audit,
    IRunnerMaintenanceCommands commands)
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RestartTimeout = TimeSpan.FromSeconds(150);
    public const int MaximumLogLines = 200;

    /// <summary>Every machine-management Runner with whether the app may maintain it.</summary>
    public async Task<IReadOnlyList<RunnerMaintenancePermissionDto>> ListAsync(Guid appId, CancellationToken token)
    {
        var granted = await db.AppRunnerPermissions.AsNoTracking()
            .Where(item => item.AppId == appId && item.Permission == AppPermissionApproval.RunnerMaintain)
            .Select(item => item.RunnerId).ToListAsync(token);
        var runners = await HostRunners().OrderBy(item => item.Name).Select(item => new { item.Id, item.Name }).ToListAsync(token);
        return runners.Select(item => new RunnerMaintenancePermissionDto(item.Id, item.Name, granted.Contains(item.Id))).ToList();
    }

    /// <summary>Approves or revokes <c>runner:maintain</c> for one app on one machine (system administrator).</summary>
    public async Task<IReadOnlyList<RunnerMaintenancePermissionDto>> SetAsync(Guid appId, Guid runnerId, bool approved, CancellationToken token)
    {
        if (!await db.WebApps.AnyAsync(app => app.Id == appId, token)) throw new KeyNotFoundException("App not found.");
        var runner = await HostRunners().Where(item => item.Id == runnerId).Select(item => new { item.Id, item.Name }).SingleOrDefaultAsync(token)
            ?? throw new KeyNotFoundException("That machine was not found.");
        var row = await db.AppRunnerPermissions.SingleOrDefaultAsync(item => item.AppId == appId && item.RunnerId == runnerId
            && item.Permission == AppPermissionApproval.RunnerMaintain, token);
        if (approved && row is null)
            db.AppRunnerPermissions.Add(new AppRunnerPermission
            {
                Id = Guid.NewGuid(), AppId = appId, RunnerId = runnerId, Permission = AppPermissionApproval.RunnerMaintain, ApprovedAtUtc = DateTime.UtcNow
            });
        else if (!approved && row is not null) db.AppRunnerPermissions.Remove(row);
        else return await ListAsync(appId, token);
        await db.SaveChangesAsync(token);
        await audit.WriteAsync(approved ? AuditActions.Approved : AuditActions.Revoked, AuditEntityTypes.AppPermission, appId,
            $"App permission {AppPermissionApproval.RunnerMaintain} was {(approved ? "approved" : "revoked")} for Runner {runner.Name}",
            new { permission = AppPermissionApproval.RunnerMaintain, appId, runnerId, runner = runner.Name }, cancellationToken: token);
        return await ListAsync(appId, token);
    }

    /// <summary>Whether the app may maintain this machine.</summary>
    public Task<bool> IsApprovedAsync(Guid appId, Guid runnerId, CancellationToken token)
        => db.AppRunnerPermissions.AnyAsync(item => item.AppId == appId && item.RunnerId == runnerId
            && item.Permission == AppPermissionApproval.RunnerMaintain, token);

    /// <summary>Queues the Docker cleanup on one machine and records who asked in the audit log.</summary>
    public async Task<ServerCleanupRunDto> RequestCleanupAsync(Guid appId, Guid viewerId, Guid runnerId, DateTime now, CancellationToken token)
    {
        var run = await cleanup.RunOnRunnerAsync(runnerId, now, token);
        await audit.WriteAsync(AuditActions.Requested, AuditEntityTypes.Runner, runnerId,
            $"Docker cleanup requested on Runner {run.RunnerName} by an app for a system administrator",
            new { appId, viewerId, runnerId, runId = run.Id, operation = "host-cleanup" }, cancellationToken: token);
        return run;
    }

    /// <summary>The apps with a deployment target on one machine, for the admin to pick one.</summary>
    public async Task<IReadOnlyList<RunnerAppDto>> ListAppsAsync(Guid runnerId, CancellationToken token)
        => await db.AppDeploymentTargets.AsNoTracking()
            .Where(target => target.ExecutionRunnerId == runnerId && target.GenericRuntimeDeployed && !target.CleanupPending)
            .OrderBy(target => target.WebApp.Name)
            .Select(target => new RunnerAppDto(target.WebAppId, target.Id, target.WebApp.Name, target.Name, target.Status.ToString()))
            .ToListAsync(token);

    /// <summary>Reads the last lines of one app's container on this machine; audit-logged.</summary>
    public async Task<ReadAppLogsResult> ReadLogsAsync(Guid appId, Guid viewerId, Guid runnerId, Guid targetId, int lines, CancellationToken token)
    {
        if (lines is < 1 or > MaximumLogLines) throw new ArgumentException($"Lines must be from 1 to {MaximumLogLines}.");
        var target = await FindTargetAsync(runnerId, targetId, token);
        var result = await SendAsync<ReadAppLogsResult>(runnerId, viewerId, "app-logs", new ReadAppLogsRequest(target.WebAppId, target.Id, lines), ReadTimeout, token);
        await AuditAsync(appId, viewerId, runnerId, target, "app-logs", $"Logs of app {target.AppName} read on a machine by an app for a system administrator", token);
        return result;
    }

    /// <summary>Restarts one app's container on this machine and waits until it is back; audit-logged.</summary>
    public async Task<RunnerAppRestartDto> RestartAppAsync(Guid appId, Guid viewerId, Guid runnerId, Guid targetId, CancellationToken token)
    {
        var target = await FindTargetAsync(runnerId, targetId, token);
        await AuditAsync(appId, viewerId, runnerId, target, "app-restart", $"Restart of app {target.AppName} requested on a machine by an app for a system administrator", token);
        var result = await SendAsync<RestartAppRuntimeResult>(runnerId, viewerId, AppRuntimeRestarter.CommandKind,
            new RestartAppRuntimeRequest(target.WebAppId, target.Id), RestartTimeout, token);
        return new(target.WebAppId, target.Id, result.Restarted);
    }

    /// <summary>Docker's disk use on this machine; audit-logged.</summary>
    public async Task<HostDiskReport> DiskReportAsync(Guid appId, Guid viewerId, Guid runnerId, CancellationToken token)
    {
        if (!await HostRunners().AnyAsync(item => item.Id == runnerId, token)) throw new KeyNotFoundException("That machine was not found.");
        var report = await SendAsync<HostDiskReport>(runnerId, viewerId, "disk-report", new { }, ReadTimeout, token);
        await audit.WriteAsync(AuditActions.Requested, AuditEntityTypes.Runner, runnerId, "Docker disk report read on a machine by an app for a system administrator",
            new { appId, viewerId, runnerId, operation = "disk-report" }, cancellationToken: token);
        return report;
    }

    private async Task<T> SendAsync<T>(Guid runnerId, Guid viewerId, string kind, object payload, TimeSpan timeout, CancellationToken token)
    {
        try
        {
            var json = await commands.SendAsync(runnerId, viewerId, AppActionCommand.Create(kind, payload), timeout, token);
            return json.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("The Runner answered with nothing.");
        }
        catch (RuntimeCommandFailedException exception) { throw new InvalidOperationException(exception.Message, exception); }
    }

    private async Task<TargetRow> FindTargetAsync(Guid runnerId, Guid targetId, CancellationToken token)
        => await db.AppDeploymentTargets.AsNoTracking()
            .Where(target => target.Id == targetId && target.ExecutionRunnerId == runnerId && target.GenericRuntimeDeployed && !target.CleanupPending)
            .Select(target => new TargetRow(target.Id, target.WebAppId, target.WebApp.Name)).SingleOrDefaultAsync(token)
            ?? throw new KeyNotFoundException("That app is not on this machine.");

    private Task AuditAsync(Guid appId, Guid viewerId, Guid runnerId, TargetRow target, string operation, string message, CancellationToken token)
        => audit.WriteAsync(AuditActions.Requested, AuditEntityTypes.Runner, runnerId, message,
            new { appId, viewerId, runnerId, targetAppId = target.WebAppId, targetId = target.Id, operation }, cancellationToken: token);

    private sealed record TargetRow(Guid Id, Guid WebAppId, string AppName);

    private IQueryable<Runner> HostRunners() => db.Runners.AsNoTracking().Where(item =>
        item.ApplicationProfile == RunnerApplicationProfiles.MachineManagement && item.DeletionRequestedAtUtc == null
        && item.InstallationId != null && item.InstallationId != "");
}
