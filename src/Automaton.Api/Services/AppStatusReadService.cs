using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Read-only status of every app on the server, for apps with an approved <c>apps:read-status</c> permission. No secrets, settings, source or logs.</summary>
public sealed class AppStatusReadService(ApplicationDbContext db)
{
    public async Task<IReadOnlyList<AppStatusItem>> ListAsync(CancellationToken token)
    {
        var apps = await db.WebApps.AsNoTracking().OrderBy(app => app.Name)
            .Select(app => new
            {
                app.Id,
                app.Name,
                app.IsPaused,
                Live = app.DeploymentTargets.Where(target => target.IsDefault)
                    .Select(target => new { target.Status, target.LastSuccessfulDeploymentAt, target.Slug }).FirstOrDefault(),
            }).ToListAsync(token);

        var result = new List<AppStatusItem>(apps.Count);
        foreach (var app in apps)
        {
            string? error = null;
            if (app.Live?.Status == DeploymentTargetStatus.Failed)
                error = await db.DeploymentOperations.AsNoTracking()
                    .Where(o => o.WebAppId == app.Id && (o.TargetSlug == null || o.TargetSlug == app.Live.Slug))
                    .OrderByDescending(o => o.CreatedAtUtc)
                    .Select(o => o.Status == DeploymentOperationStatuses.Failed ? o.ErrorMessage ?? "The deploy failed." : null)
                    .FirstOrDefaultAsync(token);
            var run = await db.AppModuleExecutions.AsNoTracking()
                .Where(execution => execution.AppId == app.Id)
                .OrderByDescending(execution => execution.CreatedAtUtc)
                .Select(execution => new AppListActionRunDto { ModuleKey = execution.ModuleKey, Status = execution.Status, CreatedAtUtc = execution.CreatedAtUtc })
                .FirstOrDefaultAsync(token);
            var next = await db.ActionSchedules.AsNoTracking()
                .Where(schedule => schedule.ApplicationId == app.Id && schedule.Enabled && schedule.NextRunAtUtc != null)
                .MinAsync(schedule => schedule.NextRunAtUtc, token);
            result.Add(new AppStatusItem(app.Id, app.Name, app.IsPaused, app.Live?.Status.ToString() ?? "Unassigned",
                app.Live?.LastSuccessfulDeploymentAt?.UtcDateTime, error, run, next));
        }
        return result;
    }
}

public sealed record AppStatusItem(Guid Id, string Name, bool IsPaused, string LiveStatus, DateTime? LastSuccessfulDeploymentAtUtc,
    string? LastDeploymentError, AppListActionRunDto? LastActionRun, DateTime? NextActionRunAtUtc);
