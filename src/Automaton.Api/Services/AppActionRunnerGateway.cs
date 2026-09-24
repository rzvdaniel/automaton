using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Services;

/// <summary>Owns SignalR contracts for reading and controlling App Actions on a Runner.</summary>
public sealed class AppActionRunnerGateway(IHubContext<AutomatonHub> hub)
{
    public Task<AppActionModulesResponse> ListModulesAsync(
        string connectionId,
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        CancellationToken cancellationToken)
        => hub.Clients.Client(connectionId).InvokeAsync<AppActionModulesResponse>(
            AutomatonHubMethods.ListAppActionModules,
            new AppActionModuleRequest(appId, deploymentTargetId, actionKey),
            cancellationToken);

    public Task<AppActionVersionsResponse> ListVersionsAsync(
        string connectionId,
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        CancellationToken cancellationToken)
        => hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionsResponse>(
            AutomatonHubMethods.ListAppActionVersions,
            new AppActionModuleRequest(appId, deploymentTargetId, actionKey),
            cancellationToken);

    public Task<AppActionVersionOperationResponse> RestoreVersionAsync(
        string connectionId,
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string version,
        CancellationToken cancellationToken)
        => hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionOperationResponse>(
            AutomatonHubMethods.RestoreAppActionVersion,
            new AppActionVersionRequest(appId, deploymentTargetId, actionKey, version),
            cancellationToken);

    public Task<AppActionVersionOperationResponse> DeleteVersionAsync(
        string connectionId,
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        string version,
        CancellationToken cancellationToken)
        => hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionOperationResponse>(
            AutomatonHubMethods.DeleteAppActionVersion,
            new AppActionVersionRequest(appId, deploymentTargetId, actionKey, version),
            cancellationToken);

    public Task<bool> CancelExecutionAsync(
        string connectionId,
        Guid executionId,
        CancellationToken cancellationToken)
        => hub.Clients.Client(connectionId).InvokeAsync<bool>(
            AutomatonHubMethods.CancelAppModuleExecution,
            new CancelAppModuleExecutionRequest(executionId),
            cancellationToken);

    public Task<AppModuleExecutionArtifactResponse> ReadExecutionArtifactAsync(
        string connectionId,
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        Guid executionId,
        bool result,
        CancellationToken cancellationToken)
        => hub.Clients.Client(connectionId).InvokeAsync<AppModuleExecutionArtifactResponse>(
            result ? AutomatonHubMethods.ReadAppModuleExecutionResult : AutomatonHubMethods.ReadAppModuleExecutionLogs,
            new ReadAppModuleExecutionArtifactRequest(appId, deploymentTargetId, actionKey, executionId),
            cancellationToken);
}
