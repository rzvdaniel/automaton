using Automaton.Core.Models;
using System.Text.Json;

namespace Automaton.Api.Services;

/// <summary>Addresses App Action management by stable Runner identity using durable commands and HTTPS results.</summary>
public sealed class AppActionRunnerGateway(RunnerExecutionOperationService operations, UserContextService userContext) : IRunnerActionCommands
{
    public async Task<T> SendAsync<T>(Guid runnerId, string kind, object payload, CancellationToken token)
    {
        var id = await operations.QueueAsync(runnerId, userContext.GetUserId(),
            new(null, null, AppActionCommand.Create(kind, payload)), token);
        var result = await operations.WaitAsync(id, token);
        if (result.Outcome != "Succeeded" || result.ActionResult is null)
            throw new InvalidOperationException(result.Error ?? "Runner Action management did not complete.");
        return result.ActionResult.Value.Deserialize<T>() ?? throw new InvalidDataException("Runner Action response is missing.");
    }

    public Task<Guid> QueueExecutionAsync(
        Guid runnerId, Guid requestedByUserId, Guid operationId,
        ExecuteAppActionModuleRequest request, CancellationToken token)
        => operations.QueueAsync(runnerId, requestedByUserId,
            new(null, null, AppActionCommand.Create("execute", request)), token, operationId);

    public Task<AppActionModulesResponse> ListModulesAsync(Guid runnerId, Guid appId, Guid targetId, string key, CancellationToken token)
        => SendAsync<AppActionModulesResponse>(runnerId, "list-modules", new AppActionModuleRequest(appId, targetId, key), token);
    public Task<AppActionVersionsResponse> ListVersionsAsync(Guid runnerId, Guid appId, Guid targetId, string key, CancellationToken token)
        => SendAsync<AppActionVersionsResponse>(runnerId, "list-versions", new AppActionModuleRequest(appId, targetId, key), token);
    public Task<AppActionVersionOperationResponse> RestoreVersionAsync(Guid runnerId, Guid appId, Guid targetId, string key, string version, CancellationToken token)
        => SendAsync<AppActionVersionOperationResponse>(runnerId, "restore-version", new AppActionVersionRequest(appId, targetId, key, version), token);
    public Task<AppActionVersionOperationResponse> DeleteVersionAsync(Guid runnerId, Guid appId, Guid targetId, string key, string version, CancellationToken token)
        => SendAsync<AppActionVersionOperationResponse>(runnerId, "delete-version", new AppActionVersionRequest(appId, targetId, key, version), token);
    public async Task<bool> CancelExecutionAsync(Guid runnerId, Guid executionId, CancellationToken token)
    {
        return await operations.CancelAsync(executionId, token);
    }
    public Task<AppModuleExecutionArtifactResponse> ReadExecutionArtifactAsync(Guid runnerId, Guid appId, Guid targetId, string key, Guid executionId, bool result, CancellationToken token)
        => SendAsync<AppModuleExecutionArtifactResponse>(runnerId, result ? "read-result" : "read-logs",
            new ReadAppModuleExecutionArtifactRequest(appId, targetId, key, executionId), token);
}
