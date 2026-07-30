using System.Text.RegularExpressions;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed partial class AppDeploymentTargetManagementService(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService,
    IAppRunnerAuthorizationPolicy runnerAuthorizationPolicy,
    IHubContext<AutomatonHub> hubContext,
    BackendRouteConfigService routeConfigService)
{
    public async Task<IReadOnlyList<AppDeploymentTargetSummary>> ListAsync(
        Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        await EnsureCanAccessAsync(appId, userId, cancellationToken);
        var targets = await TargetQuery(appId)
            .OrderByDescending(target => target.IsDefault)
            .ThenBy(target => target.Name)
            .ToListAsync(cancellationToken);
        return targets.Select(ToSummary).ToList();
    }

    public async Task<AppDeploymentTargetSummary> GetAsync(
        Guid appId, Guid targetId, Guid userId, CancellationToken cancellationToken = default)
    {
        await EnsureCanAccessAsync(appId, userId, cancellationToken);
        return ToSummary(await GetTargetAsync(appId, targetId, cancellationToken));
    }

    public async Task<AppDeploymentTargetSummary> CreateAsync(
        Guid appId,
        CreateAppDeploymentTargetRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var app = await GetAppForUpdateAsync(appId, userId, cancellationToken);
        var name = NormalizeName(request.Name);
        var slug = NormalizeSlug(request.Slug);
        await EnsureSlugAvailableAsync(appId, slug, null, cancellationToken);

        Runner? runner = null;
        if (request.RunnerId.HasValue)
        {
            runner = await runnerAuthorizationPolicy.AuthorizeAsync(
                app, request.RunnerId.Value, userId, cancellationToken);
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        if (request.IsDefault)
        {
            await ClearDefaultAsync(appId, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var target = new AppDeploymentTarget
        {
            Id = Guid.NewGuid(),
            WebAppId = appId,
            Name = name,
            Slug = slug,
            ExecutionRunnerId = runner?.Id,
            IsDefault = request.IsDefault,
            Status = runner == null
                ? DeploymentTargetStatus.Unassigned
                : DeploymentTargetStatus.NotDeployed,
            CreatedAt = now,
            UpdatedAt = now
        };
        target.SynchronizeDefaultConstraint();
        dbContext.AppDeploymentTargets.Add(target);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        if (request.IsDefault)
        {
            await routeConfigService.RefreshAsync(cancellationToken);
        }
        return await GetAsync(appId, target.Id, userId, cancellationToken);
    }

    public async Task<AppDeploymentTargetSummary> UpdateAsync(
        Guid appId,
        Guid targetId,
        UpdateAppDeploymentTargetRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _ = await GetAppForUpdateAsync(appId, userId, cancellationToken);
        var target = await GetTrackedTargetAsync(appId, targetId, cancellationToken);
        if (request.Name != null)
        {
            target.Name = NormalizeName(request.Name);
        }
        if (request.Slug != null)
        {
            var slug = NormalizeSlug(request.Slug);
            await EnsureSlugAvailableAsync(appId, slug, targetId, cancellationToken);
            target.Slug = slug;
        }
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await routeConfigService.RefreshAsync(cancellationToken);
        return await GetAsync(appId, targetId, userId, cancellationToken);
    }

    public async Task<AppDeploymentTargetSummary> MakeDefaultAsync(
        Guid appId, Guid targetId, Guid userId, CancellationToken cancellationToken = default)
    {
        _ = await GetAppForUpdateAsync(appId, userId, cancellationToken);
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var target = await GetTrackedTargetAsync(appId, targetId, cancellationToken);
        if (!target.IsDefault)
        {
            await ClearDefaultAsync(appId, cancellationToken);
            target.IsDefault = true;
            target.SynchronizeDefaultConstraint();
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        await routeConfigService.RefreshAsync(cancellationToken);
        return await GetAsync(appId, targetId, userId, cancellationToken);
    }

    public async Task<AppDeploymentTargetSummary> AssignRunnerAsync(
        Guid appId,
        Guid targetId,
        AssignAppDeploymentTargetRunnerRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var app = await GetAppForUpdateAsync(appId, userId, cancellationToken);
        var target = await GetTrackedTargetAsync(appId, targetId, cancellationToken);
        if (target.Status == DeploymentTargetStatus.Deploying)
        {
            throw new AppDeploymentTargetConflictException(
                "The Runner cannot be changed while this target is deploying.");
        }

        Runner? runner = null;
        if (request.RunnerId.HasValue)
        {
            runner = await runnerAuthorizationPolicy.AuthorizeAsync(
                app, request.RunnerId.Value, userId, cancellationToken);
        }
        if (target.ExecutionRunnerId == runner?.Id)
        {
            return ToSummary(target);
        }

        if (IsDeployed(target))
        {
            if (!request.CleanupDeployedRuntime)
            {
                throw new AppDeploymentTargetConflictException(
                    "This target is deployed. Explicit runtime cleanup is required before changing its Runner.");
            }
            await CleanupRuntimeAsync(target, cancellationToken);
            ResetDeploymentState(target);
        }

        target.ExecutionRunnerId = runner?.Id;
        target.ExecutionRunner = runner;
        target.Status = runner == null
            ? DeploymentTargetStatus.Unassigned
            : DeploymentTargetStatus.NotDeployed;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await routeConfigService.RefreshAsync(cancellationToken);
        return await GetAsync(appId, targetId, userId, cancellationToken);
    }

    public async Task DeleteAsync(
        Guid appId, Guid targetId, Guid userId, CancellationToken cancellationToken = default)
    {
        _ = await GetAppForUpdateAsync(appId, userId, cancellationToken);
        var target = await GetTrackedTargetAsync(appId, targetId, cancellationToken);
        if (target.IsDefault)
        {
            throw new AppDeploymentTargetConflictException(
                "The default deployment target cannot be deleted. Make another target default first.");
        }
        if (target.Status == DeploymentTargetStatus.Deploying)
        {
            throw new AppDeploymentTargetConflictException(
                "A deployment target cannot be deleted while it is deploying.");
        }
        if (IsDeployed(target))
        {
            await CleanupRuntimeAsync(target, cancellationToken);
        }

        dbContext.AppDeploymentTargets.Remove(target);
        await dbContext.SaveChangesAsync(cancellationToken);
        await routeConfigService.RefreshAsync(cancellationToken);
    }

    private IQueryable<AppDeploymentTarget> TargetQuery(Guid appId) =>
        dbContext.AppDeploymentTargets
            .AsNoTracking()
            .Include(target => target.ExecutionRunner)
            .Where(target => target.WebAppId == appId);

    private async Task<WebApp> GetAppForUpdateAsync(
        Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanUpdateAppAsync(appId, userId, cancellationToken))
        {
            throw new KeyNotFoundException("App not found.");
        }
        return await dbContext.WebApps.SingleOrDefaultAsync(app => app.Id == appId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
    }

    private async Task EnsureCanAccessAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanAccessAppAsync(appId, userId, cancellationToken))
        {
            throw new KeyNotFoundException("App not found.");
        }
    }

    private async Task<AppDeploymentTarget> GetTargetAsync(
        Guid appId, Guid targetId, CancellationToken cancellationToken) =>
        await TargetQuery(appId).SingleOrDefaultAsync(target => target.Id == targetId, cancellationToken)
            ?? throw new KeyNotFoundException("Deployment target not found.");

    private async Task<AppDeploymentTarget> GetTrackedTargetAsync(
        Guid appId, Guid targetId, CancellationToken cancellationToken) =>
        await dbContext.AppDeploymentTargets
            .Include(target => target.ExecutionRunner)
            .SingleOrDefaultAsync(
                target => target.WebAppId == appId && target.Id == targetId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Deployment target not found.");

    private async Task EnsureSlugAvailableAsync(
        Guid appId, string slug, Guid? excludingTargetId, CancellationToken cancellationToken)
    {
        if (await dbContext.AppDeploymentTargets.AnyAsync(
                target => target.WebAppId == appId &&
                          target.Slug == slug &&
                          target.Id != excludingTargetId,
                cancellationToken))
        {
            throw new AppDeploymentTargetConflictException(
                $"Deployment target slug '{slug}' is already in use.");
        }
    }

    private async Task ClearDefaultAsync(Guid appId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE AppDeploymentTargets
                SET IsDefault = FALSE,
                    DefaultConstraintKey = NULL,
                    UpdatedAt = {now}
                WHERE WebAppId = {appId}
                  AND IsDefault = TRUE
                """,
                cancellationToken);
            return;
        }

        var currentDefault = await dbContext.AppDeploymentTargets
            .SingleAsync(target => target.WebAppId == appId && target.IsDefault, cancellationToken);
        currentDefault.IsDefault = false;
        currentDefault.SynchronizeDefaultConstraint();
        currentDefault.UpdatedAt = now;
    }

    private async Task CleanupRuntimeAsync(
        AppDeploymentTarget target, CancellationToken cancellationToken)
    {
        var runner = target.ExecutionRunner;
        if (runner == null || string.IsNullOrWhiteSpace(runner.ConnectionId) ||
            !string.Equals(runner.Status, RunnerStatus.Online.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new AppDeploymentTargetConflictException(
                "The assigned Runner must be online to clean up this deployed target.");
        }

        var result = await hubContext.Clients.Client(runner.ConnectionId)
            .InvokeAsync<CleanupAppDeploymentTargetResult>(
                AutomatonHubMethods.CleanupAppDeploymentTarget,
                new CleanupAppDeploymentTargetRequest(target.WebAppId, target.Id),
                cancellationToken);
        if (!result.Success ||
            result.AppId != target.WebAppId ||
            result.DeploymentTargetId != target.Id)
        {
            throw new AppDeploymentTargetConflictException(
                result.Error ?? "The Runner could not clean up this deployment target.");
        }
    }

    private static void ResetDeploymentState(AppDeploymentTarget target)
    {
        target.FrontendDeployed = false;
        target.BackendDeployed = false;
        target.LastDeploymentAt = null;
        target.LastSuccessfulDeploymentAt = null;
    }

    private static bool IsDeployed(AppDeploymentTarget target) =>
        target.FrontendDeployed || target.BackendDeployed;

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 120)
        {
            throw new ArgumentException("Target name must contain between 1 and 120 characters.");
        }
        return normalized;
    }

    public static string NormalizeSlug(string slug)
    {
        var normalized = (slug?.Trim() ?? string.Empty).ToLowerInvariant();
        if (normalized.Length is < 1 or > 120 || !TargetSlugPattern().IsMatch(normalized))
        {
            throw new ArgumentException(
                "Target slug must use lowercase letters, numbers, and single hyphens between segments.");
        }
        return normalized;
    }

    private static AppDeploymentTargetSummary ToSummary(AppDeploymentTarget target) =>
        new(
            target.Id,
            target.WebAppId,
            target.Name,
            target.Slug,
            target.IsDefault,
            target.Status,
            target.FrontendDeployed,
            target.BackendDeployed,
            target.LastDeploymentAt,
            target.LastSuccessfulDeploymentAt,
            target.ExecutionRunner == null
                ? null
                : new AppDeploymentTargetRunnerSummary(
                    target.ExecutionRunner.Id,
                    target.ExecutionRunner.Name,
                    target.ExecutionRunner.Purpose,
                    string.Equals(
                        target.ExecutionRunner.Status,
                        RunnerStatus.Online.ToString(),
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(target.ExecutionRunner.ConnectionId)),
            target.IsDefault
                ? $"/private/{target.WebAppId:D}"
                : $"/a/{target.WebAppId:D}/t/{target.Id:D}/");

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetSlugPattern();
}
