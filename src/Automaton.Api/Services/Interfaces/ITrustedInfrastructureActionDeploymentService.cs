using Automaton.Core.Models;

namespace Automaton.Api.Services.Interfaces;

public interface ITrustedInfrastructureActionDeploymentService
{
    Task<AppActionModuleInfo> EnsureDeployedAsync(string connectionId, string actionKey, CancellationToken cancellationToken);
}
