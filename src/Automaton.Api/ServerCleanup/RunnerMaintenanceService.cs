using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.ServerCleanup;

public sealed record RunnerMaintenancePermissionDto(Guid RunnerId, string Name, bool Approved);

/// <summary>
/// Named maintenance operations an app may run on one chosen machine. The app needs the <c>runner:maintain</c> permission
/// approved for the app and, for each machine, a per-Runner approval; every request is audit-logged. The operations are
/// fixed commands the Runner knows; an app never sends a shell command.
/// </summary>
public sealed class RunnerMaintenanceService(ApplicationDbContext db, ServerCleanupService cleanup, IAuditEventService audit)
{
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

    private IQueryable<Runner> HostRunners() => db.Runners.AsNoTracking().Where(item =>
        item.ApplicationProfile == RunnerApplicationProfiles.MachineManagement && item.DeletionRequestedAtUtc == null
        && item.InstallationId != null && item.InstallationId != "");
}
