using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Services;

public static class AppModuleTriggerTypes
{
    public const string Manual = AppModuleTriggerKinds.Manual;
    public const string Internal = AppModuleTriggerKinds.Internal;
    public const string Scheduled = AppModuleTriggerKinds.Scheduled;
}

public sealed class AppActionExecutionService
{
    private readonly ApplicationDbContext db;
    private readonly AppsService appsService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;
    private readonly IAppDeploymentTargetResolver deploymentTargetResolver;
    private readonly IHubContext<AutomatonHub> hub;
    private readonly IServiceScopeFactory scopeFactory;

    public AppActionExecutionService(
        ApplicationDbContext db,
        AppsService appsService,
        ResourceAuthorizationService authorizationService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy,
        IAppDeploymentTargetResolver deploymentTargetResolver,
        IHubContext<AutomatonHub> hub,
        IServiceScopeFactory scopeFactory)
    {
        this.db = db;
        this.appsService = appsService;
        this.authorizationService = authorizationService;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
        this.deploymentTargetResolver = deploymentTargetResolver;
        this.hub = hub;
        this.scopeFactory = scopeFactory;
    }

    public async Task<AppModuleExecution> ExecuteAsync(Guid appId, string actionKey, Guid userId, JsonElement input, CancellationToken cancellationToken)
        => await ExecuteAsync(appId, actionKey, userId, input, null, AppModuleTriggerTypes.Manual, cancellationToken);

    public async Task<AppModuleExecution> ExecuteScheduledAsync(
        Guid appId, string actionKey, Guid userId, JsonElement input, Guid occurrenceId, CancellationToken cancellationToken)
        => await ExecuteAsync(appId, actionKey, userId, input, occurrenceId, AppModuleTriggerTypes.Scheduled, cancellationToken);

    private async Task<AppModuleExecution> ExecuteAsync(
        Guid appId, string actionKey, Guid userId, JsonElement input, Guid? occurrenceId, string triggerType, CancellationToken cancellationToken)
    {
        if (occurrenceId.HasValue)
        {
            var existing = await db.AppModuleExecutions.SingleOrDefaultAsync(x => x.ScheduleOccurrenceId == occurrenceId, cancellationToken);
            if (existing != null) return existing;
        }
        AppActionManifestParser.ValidateKey(actionKey);
        var resolved = await ResolveActionTargetAsync(appId, actionKey, userId, cancellationToken);
        return await CreateAndDispatchAsync(
            appId, resolved.Target.Id, actionKey, userId, input, occurrenceId, triggerType,
            resolved.Runner, resolved.Action, cancellationToken);
    }

    private async Task<(Runner Runner, AppDeploymentTarget Target, AppActionModuleInfo Action)> ResolveActionTargetAsync(
        Guid appId,
        string actionKey,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        if (!await authorizationService.CanExecuteAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to execute Actions in this App.");
        var runner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
            appId,
            userId,
            requireOnline: true,
            cancellationToken: cancellationToken);
        var target = await deploymentTargetResolver.ResolveAsync(
            appId,
            targetSlug: null,
            userId,
            cancellationToken);

        var deployed = await hub.Clients.Client(runner.ConnectionId).InvokeAsync<AppActionModulesResponse>(
            AutomatonHubMethods.ListAppActionModules,
            new AppActionModuleRequest(appId, target.Id, actionKey),
            cancellationToken);
        var deployedAction = deployed.Actions.SingleOrDefault(item => string.Equals(item.Key, actionKey, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("Action is not deployed on the assigned Runner.");
        return (runner, target, deployedAction);
    }

    internal async Task<AppModuleExecution> ExecuteScheduledOnRunnerAsync(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        JsonElement input,
        Guid occurrenceId,
        Runner runner,
        AppActionModuleInfo deployedAction,
        CancellationToken cancellationToken)
    {
        var existing = await db.AppModuleExecutions.SingleOrDefaultAsync(x => x.ScheduleOccurrenceId == occurrenceId, cancellationToken);
        if (existing != null) return existing;
        if (string.IsNullOrWhiteSpace(runner.ConnectionId))
            throw new InvalidOperationException("The selected Runner is unavailable.");
        return await CreateAndDispatchAsync(
            appId, deploymentTargetId, actionKey, null, input, occurrenceId, AppModuleTriggerTypes.Scheduled, runner, deployedAction, cancellationToken);
    }

    private async Task<AppModuleExecution> CreateAndDispatchAsync(
        Guid appId,
        Guid deploymentTargetId,
        string actionKey,
        Guid? userId,
        JsonElement input,
        Guid? occurrenceId,
        string triggerType,
        Runner runner,
        AppActionModuleInfo deployedAction,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var execution = new AppModuleExecution
        {
            Id = Guid.NewGuid(), AppId = appId, DeploymentTargetId = deploymentTargetId,
            ModuleType = AppModuleTypes.Action, ModuleKey = actionKey,
            RunnerId = runner.Id, TriggerType = triggerType, RequestedByUserId = userId,
            ScheduleOccurrenceId = occurrenceId,
            Status = AppModuleExecutionStatuses.Dispatching, CreatedAtUtc = now,
            DeploymentHash = deployedAction.ContentHash,
            ExecutionDeadlineUtc = now.AddSeconds(deployedAction.TimeoutSeconds + 300),
            CorrelationId = System.Diagnostics.Activity.Current?.TraceId.ToString()
        };
        db.AppModuleExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);

        var inputJson = input.ValueKind == JsonValueKind.Undefined ? "null" : input.GetRawText();
        var executionId = execution.Id;
        var connectionId = runner.ConnectionId;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppActionExecutionDispatcher>()
                    .DispatchAsync(executionId, connectionId, inputJson);
            }
            catch
            {
                // The hosted reconciler marks the persisted record Lost if dispatch
                // fails before a terminal result is saved.
            }
        }, CancellationToken.None);
        return execution;
    }

    public async Task<IReadOnlyList<AppModuleExecution>> ListAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        _ = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        return await db.AppModuleExecutions.AsNoTracking().Where(x => x.AppId == appId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
    }

    public async Task<AppActionModulesResponse> ListModulesAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        var (connectionId, deploymentTargetId) = await GetAuthorizedRunnerConnectionAsync(appId, userId, cancellationToken);
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionModulesResponse>(
            AutomatonHubMethods.ListAppActionModules,
            new AppActionModuleRequest(appId, deploymentTargetId, string.Empty),
            cancellationToken);
    }

    public async Task<AppActionModuleInfo> GetModuleAsync(Guid appId, string actionKey, Guid userId, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        var response = await ListModulesAsync(appId, userId, cancellationToken);
        return response.Actions.SingleOrDefault(action => string.Equals(action.Key, actionKey, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("Action was not found on the assigned Runner.");
    }

    public async Task<AppActionVersionsResponse> ListVersionsAsync(Guid appId, string actionKey, Guid userId, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        var (connectionId, deploymentTargetId) = await GetAuthorizedRunnerConnectionAsync(appId, userId, cancellationToken);
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionsResponse>(
            AutomatonHubMethods.ListAppActionVersions,
            new AppActionModuleRequest(appId, deploymentTargetId, actionKey),
            cancellationToken);
    }

    public async Task<AppActionVersionOperationResponse> RestoreVersionAsync(Guid appId, string actionKey, string version, Guid userId, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        await EnsureCanUpdateAsync(appId, userId, cancellationToken);
        var (connectionId, deploymentTargetId) = await GetAuthorizedRunnerConnectionAsync(appId, userId, cancellationToken);
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionOperationResponse>(
            AutomatonHubMethods.RestoreAppActionVersion,
            new AppActionVersionRequest(appId, deploymentTargetId, actionKey, version),
            cancellationToken);
    }

    public async Task<AppActionVersionOperationResponse> DeleteVersionAsync(Guid appId, string actionKey, string version, Guid userId, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        await EnsureCanUpdateAsync(appId, userId, cancellationToken);
        var (connectionId, deploymentTargetId) = await GetAuthorizedRunnerConnectionAsync(appId, userId, cancellationToken);
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionOperationResponse>(
            AutomatonHubMethods.DeleteAppActionVersion,
            new AppActionVersionRequest(appId, deploymentTargetId, actionKey, version),
            cancellationToken);
    }

    public async Task<AppModuleExecution> GetAsync(Guid appId, Guid executionId, Guid userId, CancellationToken cancellationToken)
    {
        _ = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        return await db.AppModuleExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.AppId == appId && x.Id == executionId, cancellationToken)
            ?? throw new KeyNotFoundException("Execution not found.");
    }

    public async Task<bool> CancelAsync(Guid appId, Guid executionId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanExecuteAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to cancel Actions in this App.");
        var execution = await GetAsync(appId, executionId, userId, cancellationToken);
        if (!execution.RunnerId.HasValue) return false;
        var connectionId = await db.Runners.Where(x => x.Id == execution.RunnerId).Select(x => x.ConnectionId).SingleOrDefaultAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(connectionId) && await hub.Clients.Client(connectionId).InvokeAsync<bool>(
            AutomatonHubMethods.CancelAppModuleExecution, new CancelAppModuleExecutionRequest(executionId), cancellationToken);
    }

    public async Task<AppModuleExecutionArtifactResponse> ReadArtifactAsync(Guid appId, Guid executionId, Guid userId, bool result, CancellationToken cancellationToken)
    {
        var execution = await GetAsync(appId, executionId, userId, cancellationToken);
        if (!execution.RunnerId.HasValue) return new(false, null, null, "Runner is unavailable.");
        if (!execution.DeploymentTargetId.HasValue)
            return new(false, null, null, "Execution has no deployment target.");
        var connectionId = await db.Runners.Where(x => x.Id == execution.RunnerId).Select(x => x.ConnectionId).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(connectionId)) return new(false, null, null, "Runner is unavailable.");
        return await hub.Clients.Client(connectionId).InvokeAsync<AppModuleExecutionArtifactResponse>(
            result ? AutomatonHubMethods.ReadAppModuleExecutionResult : AutomatonHubMethods.ReadAppModuleExecutionLogs,
            new ReadAppModuleExecutionArtifactRequest(
                appId,
                execution.DeploymentTargetId.Value,
                execution.ModuleKey,
                executionId),
            cancellationToken);
    }

    private async Task<(string ConnectionId, Guid DeploymentTargetId)> GetAuthorizedRunnerConnectionAsync(
        Guid appId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var runner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
            appId,
            userId,
            requireOnline: true,
            cancellationToken: cancellationToken);
        var target = await deploymentTargetResolver.ResolveAsync(
            appId,
            targetSlug: null,
            userId,
            cancellationToken);
        return (runner.ConnectionId!, target.Id);
    }

    private async Task EnsureCanUpdateAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        _ = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        if (!await authorizationService.CanUpdateAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to deploy or modify this App.");
    }

}
