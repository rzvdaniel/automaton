using Automaton.Core.Models;

namespace Automaton.Api.Services.Interfaces;

public interface ITrustedInfrastructureActionDeploymentService
{
    Task<AppActionModuleInfo> EnsureDeployedAsync(Guid runnerId, string actionKey, CancellationToken cancellationToken);

    Task<AppActionModuleInfo> EnsureDeployedOnTargetAsync(
        Guid runnerId,
        Guid deploymentTargetId,
        string actionKey,
        CancellationToken cancellationToken);
}
