using Automaton.Core.Models;

namespace Automaton.Api.Services;

public interface IAppActionExecutionGateway
{
    Task<Guid> QueueExecutionAsync(
        Guid runnerId,
        Guid requestedByUserId,
        Guid operationId,
        ExecuteAppActionModuleRequest request,
        CancellationToken token);

    Task<AppActionModulesResponse> ListModulesAsync(
        Guid runnerId, Guid appId, Guid targetId, string key, CancellationToken token);

    Task<AppActionVersionsResponse> ListVersionsAsync(
        Guid runnerId, Guid appId, Guid targetId, string key, CancellationToken token);

    Task<AppActionVersionOperationResponse> RestoreVersionAsync(
        Guid runnerId, Guid appId, Guid targetId, string key, string version, CancellationToken token);

    Task<AppActionVersionOperationResponse> DeleteVersionAsync(
        Guid runnerId, Guid appId, Guid targetId, string key, string version, CancellationToken token);

    Task<bool> CancelExecutionAsync(Guid runnerId, Guid executionId, CancellationToken token);

    Task<AppModuleExecutionArtifactResponse> ReadExecutionArtifactAsync(
        Guid runnerId,
        Guid appId,
        Guid targetId,
        string key,
        Guid executionId,
        bool result,
        CancellationToken token);
}
