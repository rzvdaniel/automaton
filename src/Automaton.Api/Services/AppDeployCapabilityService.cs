using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

public class AppDeployCapabilityService : IAppDeployCapabilityService
{
    public Task<AppDeployCapabilitiesResponse> GetCapabilitiesAsync(
        Guid ownerId,
        Guid appId,
        CancellationToken cancellationToken = default)
    {
        // Design rule:
        // The UI never decides what is deployable. Studio always asks the backend for the
        // current capability contract, and the backend remains the source of truth.
        //
        // This service is the first implementation of that contract. When Studio gains
        // additional deploy paths (for example cloud builds or other runtime
        // targets), evolve this logic behind the same API shape instead of teaching the
        // client new deploy rules.
        //
        // Do not rush into a heavier strategy abstraction until the logic genuinely
        // starts branching. When that happens, a reasonable evolution path would be
        // interfaces such as IAppDeployCapabilityProvider and IAppDeployExecutor, backed
        // by strategies like StaticCopyDeployStrategy, CliArtifactDeployStrategy,
        // StudioCloudBuildDeployStrategy, and BackendOnlyDeployStrategy.
        // The Studio UI consumes this as a capability contract. Future deploy strategies
        // can change the decision logic here without changing the client-side meaning of
        // "can deploy the application from Studio".
        // "Deploy app" must keep a single meaning in the UI: deploy the application.
        // If Studio cannot safely complete that promise end to end, this stays false and
        // the UI surfaces exceptions contextually via Application Status instead.
        // "Deploy app" is the safe default action only when Studio can complete the full
        // path end to end. Build-required applications intentionally block that default path
        // until Studio gains its own build pipeline.
        // Studio does not yet build the canonical deploy package. Keep the
        // capability disabled instead of exposing a request path it cannot fulfill.
        var canDeployApplicationFromStudio = false;

        return Task.FromResult(new AppDeployCapabilitiesResponse
        {
            CanDeployApplicationFromStudio = canDeployApplicationFromStudio,
        });
    }
}
