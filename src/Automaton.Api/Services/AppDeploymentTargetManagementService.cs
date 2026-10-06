using System.Text.RegularExpressions;
using System.Data;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed partial class AppDeploymentTargetManagementService(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService,
    IAppRunnerAuthorizationPolicy runnerAuthorizationPolicy,
    BackendRouteConfigService routeConfigService,
    AppDeploymentTargetStateService targetStateService,
    AppDeploymentTargetRuntimeCleanupService runtimeCleanupService)
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

        await using var transaction = await new AppDeletionClaimService(dbContext)
            .OpenAdmissionAsync(appId, cancellationToken);
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
        if (runner != null)
        {
            PrivilegedHostAssignmentGuard.EnsureApproved(target, runner);
        }
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
            ? await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken)
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

    public async Task<AppDeploymentTargetSummary> SetPrivilegedHostApprovalAsync(
        Guid appId,
        Guid targetId,
        Guid approverUserId,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        var target = await GetTrackedTargetAsync(appId, targetId, cancellationToken);
        if (approved)
        {
            if (!target.PrivilegedHostAccessApproved)
            {
                target.PrivilegedHostAccessApproved = true;
                target.PrivilegedHostAccessApprovedAtUtc = DateTimeOffset.UtcNow;
                target.PrivilegedHostAccessApprovedByUserId = approverUserId;
                target.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        else if (target.PrivilegedHostAccessApproved ||
                 target.PrivilegedHostAccessApprovedAtUtc.HasValue ||
                 target.PrivilegedHostAccessApprovedByUserId.HasValue)
        {
            target.PrivilegedHostAccessApproved = false;
            target.PrivilegedHostAccessApprovedAtUtc = null;
            target.PrivilegedHostAccessApprovedByUserId = null;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToSummary(target);
    }

    public async Task<AppDeploymentTargetSummary> AssignRunnerAsync(
        Guid appId,
        Guid targetId,
        AssignAppDeploymentTargetRunnerRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var app = await GetAppForUpdateAsync(appId, userId, cancellationToken);
        var originalStatus = await targetStateService.ClaimMutationAsync(
            appId,
            targetId,
            "The Runner cannot be changed while this target is deploying.",
            cancellationToken);
        var completed = false;
        try
        {
            var target = await GetTrackedTargetAsync(appId, targetId, cancellationToken);
            Runner? runner = null;
            if (request.RunnerId.HasValue)
            {
                runner = await runnerAuthorizationPolicy.AuthorizeAsync(
                    app, request.RunnerId.Value, userId, cancellationToken);
                if (target.ExecutionRunnerId != runner.Id)
                {
                    PrivilegedHostAssignmentGuard.EnsureApproved(target, runner);
                }
            }
            if (target.ExecutionRunnerId == runner?.Id)
            {
                target.Status = originalStatus;
                await dbContext.SaveChangesAsync(cancellationToken);
                completed = true;
                return ToSummary(target);
            }

            if (IsDeployed(target))
            {
                if (!request.CleanupDeployedRuntime)
                {
                    throw new AppDeploymentTargetConflictException(
                        "This target is deployed. Explicit runtime cleanup is required before changing its Runner.");
                }
                await runtimeCleanupService.CleanupRuntimeAsync(target, cancellationToken);
                ResetDeploymentState(target);
            }

            target.ExecutionRunnerId = runner?.Id;
            target.ExecutionRunner = runner;
            target.Status = runner == null
                ? DeploymentTargetStatus.Unassigned
                : DeploymentTargetStatus.NotDeployed;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            completed = true;
            await routeConfigService.RefreshAsync(cancellationToken);
            return await GetAsync(appId, targetId, userId, cancellationToken);
        }
        finally
        {
            if (!completed)
            {
                dbContext.ChangeTracker.Clear();
                await targetStateService.ReleaseMutationAsync(
                    appId, targetId, originalStatus, CancellationToken.None);
            }
        }
    }

    public async Task DeleteAsync(
        Guid appId, Guid targetId, Guid userId, CancellationToken cancellationToken = default)
    {
        _ = await GetAppForUpdateAsync(appId, userId, cancellationToken);
        var originalStatus = await targetStateService.ClaimMutationAsync(
            appId,
            targetId,
            "A deployment target cannot be deleted while it is deploying.",
            cancellationToken);
        var deleted = false;
        try
        {
            var target = await GetTrackedTargetAsync(appId, targetId, cancellationToken);
            if (target.IsDefault)
            {
                throw new AppDeploymentTargetConflictException(
                    "The Live deployment target cannot be deleted. Promote another target to Live first.");
            }
            if (IsDeployed(target))
            {
                await runtimeCleanupService.CleanupRuntimeAsync(target, cancellationToken);
            }

            dbContext.AppDeploymentTargets.Remove(target);
            await dbContext.SaveChangesAsync(cancellationToken);
            deleted = true;
            await routeConfigService.RefreshAsync(cancellationToken);
        }
        finally
        {
            if (!deleted)
            {
                dbContext.ChangeTracker.Clear();
                await targetStateService.ReleaseMutationAsync(
                    appId, targetId, originalStatus, CancellationToken.None);
            }
        }
    }

    public async Task<IReadOnlyDictionary<Guid, DeploymentTargetStatus>> ClaimAndCleanupAppRuntimesAsync(
        Guid appId,
        CancellationToken cancellationToken = default,
        bool recoverInterrupted = false,
        Func<CancellationToken, Task>? assertDeletionOwner = null)
    {
        var targetIds = await dbContext.AppDeploymentTargets
            .AsNoTracking()
            .Where(target => target.WebAppId == appId)
            .Select(target => target.Id)
            .ToListAsync(cancellationToken);
        var claims = new Dictionary<Guid, DeploymentTargetStatus>();
        try
        {
            foreach (var targetId in targetIds)
            {
                if (assertDeletionOwner is not null) await assertDeletionOwner(cancellationToken);
                claims[targetId] = await targetStateService.ClaimMutationAsync(
                    appId,
                    targetId,
                    "The app cannot be deleted while one of its targets is deploying.",
                    cancellationToken, recoverInterrupted);
            }

            var assignedTargets = new List<AppDeploymentTarget>();
            foreach (var targetId in targetIds)
            {
                if (assertDeletionOwner is not null) await assertDeletionOwner(cancellationToken);
                var target = await GetTrackedTargetAsync(appId, targetId, cancellationToken);
                if (target.ExecutionRunnerId.HasValue)
                {
                    await runtimeCleanupService.CleanupRuntimeAsync(target, cancellationToken);
                    assignedTargets.Add(target);
                }
            }

            foreach (var runnerTargets in assignedTargets.GroupBy(target => target.ExecutionRunnerId))
            {
                if (assertDeletionOwner is not null) await assertDeletionOwner(cancellationToken);
                await runtimeCleanupService.CleanupRuntimeAsync(
                    runnerTargets.First(), cancellationToken, deleteApplicationStorage: true);
            }

            return claims;
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            await ReleaseAppDeletionClaimsAsync(appId, claims, CancellationToken.None);
            throw;
        }
    }

    public async Task ReleaseAppDeletionClaimsAsync(
        Guid appId,
        IReadOnlyDictionary<Guid, DeploymentTargetStatus> claims,
        CancellationToken cancellationToken = default)
    {
        foreach (var claim in claims)
        {
            await targetStateService.ReleaseMutationAsync(
                appId, claim.Key, claim.Value, cancellationToken);
        }
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

    private static void ResetDeploymentState(AppDeploymentTarget target)
    {
        target.GenericRuntimeDeployed = false;
        target.LastDeploymentAt = null;
        target.LastSuccessfulDeploymentAt = null;
    }

    private static bool IsDeployed(AppDeploymentTarget target) =>
        target.GenericRuntimeDeployed;

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

    private AppDeploymentTargetSummary ToSummary(AppDeploymentTarget target) =>
        new(
            target.Id,
            target.WebAppId,
            target.IsDefault && IsLegacyDefaultTarget(target) ? "Live" : target.Name,
            target.Slug,
            target.IsDefault,
            GetEffectiveStatus(target),
            target.GenericRuntimeDeployed,
            target.LastDeploymentAt,
            target.LastSuccessfulDeploymentAt,
            target.CleanupPending,
            target.LastCleanupError,
            target.LastCleanupAttemptAt,
            target.ExecutionRunner == null
                ? null
                : new AppDeploymentTargetRunnerSummary(
                    target.ExecutionRunner.Id,
                    target.ExecutionRunner.Name,
                    target.ExecutionRunner.Purpose,
                    target.ExecutionRunner.ApplicationProfile,
                    dbContext.HostedRunners.Any(hosted =>
                        hosted.RunnerId == target.ExecutionRunner.Id && hosted.DeletedAt == null),
                    string.Equals(
                        target.ExecutionRunner.Status,
                        RunnerStatus.Online.ToString(),
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(target.ExecutionRunner.ConnectionId)),
            PublishedAppHostNames.BuildPrivateTargetPath(target.WebAppId, target.Id, target.IsDefault),
            target.PrivilegedHostAccessApproved,
            target.PrivilegedHostAccessApprovedAtUtc,
            target.PrivilegedHostAccessApprovedByUserId);

    private static bool IsLegacyDefaultTarget(AppDeploymentTarget target) =>
        string.Equals(target.Name, "default", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(target.Slug, "default", StringComparison.OrdinalIgnoreCase);

    private static DeploymentTargetStatus GetEffectiveStatus(AppDeploymentTarget target)
    {
        if (target.ExecutionRunner == null)
        {
            return DeploymentTargetStatus.Unassigned;
        }

        var runnerOnline =
            string.Equals(
                target.ExecutionRunner.Status,
                RunnerStatus.Online.ToString(),
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(target.ExecutionRunner.ConnectionId);
        return !runnerOnline && IsDeployed(target)
            ? DeploymentTargetStatus.RunnerOffline
            : target.Status;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TargetSlugPattern();
}
