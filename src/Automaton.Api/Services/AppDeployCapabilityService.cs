using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

public class AppDeployCapabilityService : IAppDeployCapabilityService
{
    private readonly AppFileStorageService fileStorageService;

    public AppDeployCapabilityService(AppFileStorageService fileStorageService)
    {
        this.fileStorageService = fileStorageService;
    }

    public async Task<AppDeployCapabilitiesResponse> GetCapabilitiesAsync(
        Guid ownerId,
        Guid appId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await fileStorageService.GetAppStorageMetadataAsync(ownerId, appId);
        var frontendDeployMode = fileStorageService.GetFrontendStudioDeployMode(ownerId, appId);
        var actionsRoot = Path.Combine(fileStorageService.GetAppSourceDirectoryPath(ownerId, appId), "actions");
        var actionsAvailable = Directory.Exists(actionsRoot)
            && Directory.EnumerateDirectories(actionsRoot).Any(directory => File.Exists(Path.Combine(directory, "action.yaml")));

        // Design rule:
        // The UI never decides what is deployable. Studio always asks the backend for the
        // current capability contract, and the backend remains the source of truth.
        //
        // This service is the first implementation of that contract. When Studio gains
        // additional deploy paths (for example cloud/frontend builds or other runtime
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
        // "can deploy app/frontend/backend from Studio".
        var canDeployFrontendFromStudio =
            metadata.SourceAvailable &&
            string.Equals(frontendDeployMode, AppFrontendDeployModes.StaticCopy, StringComparison.Ordinal);

        var canDeployBackendFromStudio = metadata.SourceAvailable;

        // "Deploy app" must keep a single meaning in the UI: deploy the application.
        // If Studio cannot safely complete that promise end to end, this stays false and
        // the UI surfaces exceptions contextually via Application Status instead.
        // "Deploy app" is the safe default action only when Studio can complete the full
        // path end to end. Build-required frontends intentionally block that default path
        // until Studio gains its own build pipeline.
        var canDeployAppFromStudio =
            (canDeployFrontendFromStudio || !metadata.SourceAvailable) &&
            (canDeployBackendFromStudio || !metadata.SourceAvailable) &&
            (metadata.SourceAvailable || actionsAvailable);

        return new AppDeployCapabilitiesResponse
        {
            CanDeployAppFromStudio = canDeployAppFromStudio,
            CanDeployFrontendFromStudio = canDeployFrontendFromStudio,
            CanDeployBackendFromStudio = canDeployBackendFromStudio,
            FrontendDeployMode = frontendDeployMode,
            FrontendDeployBlockReason = !canDeployFrontendFromStudio && metadata.SourceAvailable
                ? "This app requires a frontend build. Deploy from the CLI to update the frontend."
                : null,
        };
    }
}
