using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class AppDeploymentTargetResolver(
    ApplicationDbContext dbContext,
    ResourceAuthorizationService authorizationService,
    IAppRunnerAuthorizationPolicy runnerAuthorizationPolicy) : IAppDeploymentTargetResolver
{
    public async Task<AppDeploymentTarget> ResolveForActionExecutionAsync(
        Guid appId,
        string? targetSlug,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorizationService.CanExecuteAppAsync(appId, userId, cancellationToken))
        {
            throw new KeyNotFoundException("App not found.");
        }

        var app = await dbContext.WebApps.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == appId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        var normalizedSlug = targetSlug?.Trim().ToLowerInvariant();
        var targets = dbContext.AppDeploymentTargets.AsNoTracking().Where(target => target.WebAppId == appId);
        var target = string.IsNullOrWhiteSpace(normalizedSlug)
            ? await targets.SingleOrDefaultAsync(item => item.IsDefault, cancellationToken)
            : await targets.SingleOrDefaultAsync(item => item.Slug == normalizedSlug, cancellationToken);
        if (target is null)
        {
            throw new KeyNotFoundException("The App has no matching deployment target.");
        }

        if (!target.ExecutionRunnerId.HasValue)
        {
            throw new AppDeploymentTargetUnassignedException(target.Slug);
        }

        target.ExecutionRunner = await runnerAuthorizationPolicy.AuthorizeActionExecutionAsync(
            app, target.ExecutionRunnerId.Value, userId, cancellationToken);
        return target;
    }

    public async Task<AppDeploymentTarget> ResolveByIdAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorizationService.CanAccessAppAsync(appId, userId, cancellationToken))
        {
            throw new KeyNotFoundException("App not found.");
        }

        var app = await dbContext.WebApps
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == appId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");

        var target = await dbContext.AppDeploymentTargets
            .AsNoTracking()
            .SingleOrDefaultAsync(target =>
                target.Id == deploymentTargetId && target.WebAppId == appId,
                cancellationToken)
            ?? throw new KeyNotFoundException("Deployment target was not found for this App.");

        if (!target.ExecutionRunnerId.HasValue)
        {
            throw new AppDeploymentTargetUnassignedException(target.Slug);
        }

        target.ExecutionRunner = await runnerAuthorizationPolicy.AuthorizeAsync(
            app,
            target.ExecutionRunnerId.Value,
            userId,
            cancellationToken);

        return target;
    }

    public async Task<AppDeploymentTarget> ResolveAsync(
        Guid appId,
        string? targetSlug,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await authorizationService.CanAccessAppAsync(appId, userId, cancellationToken))
        {
            throw new KeyNotFoundException("App not found.");
        }

        var app = await dbContext.WebApps
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == appId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");

        var normalizedSlug = targetSlug?.Trim().ToLowerInvariant();
        var targets = dbContext.AppDeploymentTargets
            .AsNoTracking()
            .Where(target => target.WebAppId == appId);

        var target = string.IsNullOrWhiteSpace(normalizedSlug)
            ? await targets.SingleOrDefaultAsync(target => target.IsDefault, cancellationToken)
            : await targets.SingleOrDefaultAsync(target => target.Slug == normalizedSlug, cancellationToken);

        if (target == null)
        {
            throw new KeyNotFoundException(
                string.IsNullOrWhiteSpace(normalizedSlug)
                    ? "The App has no default deployment target."
                    : $"Deployment target '{normalizedSlug}' was not found for this App.");
        }

        if (!target.ExecutionRunnerId.HasValue)
        {
            throw new AppDeploymentTargetUnassignedException(target.Slug);
        }

        target.ExecutionRunner = await runnerAuthorizationPolicy.AuthorizeAsync(
            app,
            target.ExecutionRunnerId.Value,
            userId,
            cancellationToken);

        return target;
    }
}
