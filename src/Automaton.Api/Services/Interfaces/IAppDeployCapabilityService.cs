using Automaton.Api.Models;

namespace Automaton.Api.Services.Interfaces;

public interface IAppDeployCapabilityService
{
    Task<AppDeployCapabilitiesResponse> GetCapabilitiesAsync(Guid ownerId, Guid appId, CancellationToken cancellationToken = default);
}
