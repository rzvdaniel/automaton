using Automaton.Api.Config;
using Automaton.Core.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Api.Services;

/// <summary>Addresses App Action management by stable Runner identity using durable commands and HTTPS results.</summary>
public sealed class AppActionRunnerGateway(
    RunnerExecutionOperationService operations, UserContextService userContext, IOptions<HostedRunnerOptions> options)
    : IRunnerActionCommands, IAppActionExecutionGateway
{
    /// <summary>Sends a command and waits for the Runner's answer without a limit; used for long operations such as cleanup.</summary>
    public Task<T> SendAsync<T>(Guid runnerId, string kind, object payload, CancellationToken token)
        => SendAsync<T>(runnerId, kind, payload, limit: null, token);

    // A Runner that stopped recently still looks online for a while, so quick commands wait only a bounded time
    // instead of holding the caller's request open.
    private Task<T> SendQuickAsync<T>(Guid runnerId, string kind, object payload, CancellationToken token)
        => SendAsync<T>(runnerId, kind, payload, TimeSpan.FromSeconds(Math.Max(1, options.Value.ActionCommandTimeoutSeconds)), token);

    private async Task<T> SendAsync<T>(Guid runnerId, string kind, object payload, TimeSpan? limit, CancellationToken token)
    {
        var id = await operations.QueueAsync(runnerId, userContext.GetUserId(),
            new(null, null, AppActionCommand.Create(kind, payload)), token);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (limit is { } wait) timer.CancelAfter(wait);
        RunnerExecutionResult result;
        try
        {
            result = await operations.WaitAsync(id, timer.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && limit is not null)
        {
            throw new TimeoutException(
                $"The Runner assigned to this app did not answer within {limit.Value.TotalSeconds:0} seconds. It may be offline; try again when it is back.");
        }
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
        => SendQuickAsync<AppActionModulesResponse>(runnerId, "list-modules", new AppActionModuleRequest(appId, targetId, key), token);
    public Task<AppActionVersionsResponse> ListVersionsAsync(Guid runnerId, Guid appId, Guid targetId, string key, CancellationToken token)
        => SendQuickAsync<AppActionVersionsResponse>(runnerId, "list-versions", new AppActionModuleRequest(appId, targetId, key), token);
    public Task<AppActionVersionOperationResponse> RestoreVersionAsync(Guid runnerId, Guid appId, Guid targetId, string key, string version, CancellationToken token)
        => SendQuickAsync<AppActionVersionOperationResponse>(runnerId, "restore-version", new AppActionVersionRequest(appId, targetId, key, version), token);
    public Task<AppActionVersionOperationResponse> DeleteVersionAsync(Guid runnerId, Guid appId, Guid targetId, string key, string version, CancellationToken token)
        => SendQuickAsync<AppActionVersionOperationResponse>(runnerId, "delete-version", new AppActionVersionRequest(appId, targetId, key, version), token);
    public async Task<bool> CancelExecutionAsync(Guid runnerId, Guid executionId, CancellationToken token)
    {
        return await operations.CancelAsync(executionId, token);
    }
    public Task<AppModuleExecutionArtifactResponse> ReadExecutionArtifactAsync(Guid runnerId, Guid appId, Guid targetId, string key, Guid executionId, bool result, CancellationToken token)
        => SendQuickAsync<AppModuleExecutionArtifactResponse>(runnerId, result ? "read-result" : "read-logs",
            new ReadAppModuleExecutionArtifactRequest(appId, targetId, key, executionId), token);
}
