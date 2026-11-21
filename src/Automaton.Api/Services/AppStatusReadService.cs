using Automaton.Core.Models;
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
                app.Category,
                app.InstalledFromRepo,
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
            error = Summarize(error);
            var run = await LastRunAsync(app.Id, token);
            var next = await NextRunAsync(app.Id, token);
            result.Add(new AppStatusItem(app.Id, app.Name, app.IsPaused, app.Live?.Status.ToString() ?? "Unassigned",
                app.Live?.LastSuccessfulDeploymentAt?.UtcDateTime, error, run, next, app.InstalledFromRepo is null ? null : app.Category));
        }
        return result;
    }

    /// <summary>The app's latest Action run; the platform's own card refreshes do not count.</summary>
    private Task<AppListActionRunDto?> LastRunAsync(Guid appId, CancellationToken token)
        => db.AppModuleExecutions.AsNoTracking()
            .Where(execution => execution.AppId == appId && execution.ModuleKey != Automaton.Core.Models.AppHomeCardLimits.ActionKey)
            .OrderByDescending(execution => execution.CreatedAtUtc)
            .Select(execution => new AppListActionRunDto { ModuleKey = execution.ModuleKey, Status = execution.Status, CreatedAtUtc = execution.CreatedAtUtc })
            .FirstOrDefaultAsync(token);

    private Task<DateTime?> NextRunAsync(Guid appId, CancellationToken token)
        => db.ActionSchedules.AsNoTracking()
            .Where(schedule => schedule.ApplicationId == appId && schedule.Enabled && schedule.NextRunAtUtc != null
                && (schedule.SystemIdentity == null || !schedule.SystemIdentity.StartsWith(Automaton.Api.Entities.HomeCardScheduleIdentity.Prefix)))
            .MinAsync(schedule => schedule.NextRunAtUtc, token);

    /// <summary>The first line of a deploy error, cut short, so build output, paths and stack traces stay on the server.</summary>
    internal static string? Summarize(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;
        var line = error.Split('\n', 2)[0].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}

public sealed record AppStatusItem(Guid Id, string Name, bool IsPaused, string LiveStatus, DateTime? LastSuccessfulDeploymentAtUtc,
    string? LastDeploymentError, AppListActionRunDto? LastActionRun, DateTime? NextActionRunAtUtc, string? Category = null);
