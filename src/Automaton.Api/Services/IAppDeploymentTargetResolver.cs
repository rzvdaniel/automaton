using Automaton.Api.Entities;

namespace Automaton.Api.Services;

public interface IAppDeploymentTargetResolver
{
    Task<AppDeploymentTarget> ResolveForActionExecutionAsync(
        Guid appId,
        string? targetSlug,
        Guid userId,
        CancellationToken cancellationToken = default) => ResolveAsync(appId, targetSlug, userId, cancellationToken);

    Task<AppDeploymentTarget> ResolveAsync(
        Guid appId,
        string? targetSlug,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AppDeploymentTarget> ResolveByIdAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class AppDeploymentTargetUnassignedException(string targetSlug)
    : InvalidOperationException(
        $"Deployment target '{targetSlug}' has no assigned Runner.");
