using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Core.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Automaton.Api.Models;

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
    private readonly IAppDeploymentTargetResolver deploymentTargetResolver;
    private readonly IAppActionExecutionGateway runnerGateway;
    private readonly AppSecretService? appSecrets;

    public AppActionExecutionService(
        ApplicationDbContext db,
        AppsService appsService,
        ResourceAuthorizationService authorizationService,
        IAppDeploymentTargetResolver deploymentTargetResolver,
        IAppActionExecutionGateway runnerGateway,
        IOptions<MachineContainerResourceOptions>? machineResources = null,
        AppSecretService? appSecrets = null)
    {
        this.db = db;
        this.appsService = appsService;
        this.authorizationService = authorizationService;
        this.deploymentTargetResolver = deploymentTargetResolver;
        this.runnerGateway = runnerGateway;
        this.appSecrets = appSecrets;
        this.machineResources = machineResources?.Value;
    }

    private readonly MachineContainerResourceOptions? machineResources;

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
        if (!await authorizationService.CanExecuteAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to execute Actions in this App.");
        var target = await deploymentTargetResolver.ResolveForActionExecutionAsync(
            appId, targetSlug: null, userId, cancellationToken);
        var runner = target.ExecutionRunner!;
        if (!RunnerCapabilities.Includes(
                runner.CapabilitiesJson,
                RunnerCapabilities.WorkflowExecution,
                runner.Purpose))
        {
            throw new InvalidOperationException("The assigned Runner does not support isolated Action execution.");
        }
        EnsureAvailable(runner);
        var deployed = await runnerGateway.ListModulesAsync(
            runner.Id, appId, target.Id, actionKey, cancellationToken);
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
        if (!runner.LastHeartbeatAtUtc.HasValue || runner.LastHeartbeatAtUtc < DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold)
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
        if (!RunnerCapabilities.Includes(
                runner.CapabilitiesJson,
                RunnerCapabilities.WorkflowExecution,
                runner.Purpose))
            throw new InvalidOperationException("The assigned Runner does not support isolated Action execution.");

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
        var request = new ExecuteAppActionModuleRequest
        {
            ExecutionId = execution.Id, AppId = appId, DeploymentTargetId = deploymentTargetId,
            ActionKey = actionKey, ExpectedDeploymentHash = deployedAction.ContentHash,
            Input = input.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement<object?>(null) : input.Clone(),
        };
        // The request is stored encrypted and fetched by the Runner over HTTPS; the Runner passes
        // secrets to the Action container as environment variables and redacts them from logs.
        if (appSecrets is not null)
            request.Secrets = await appSecrets.ResolveForExecutionAsync(appId, cancellationToken);
        if (RunnerApplicationProfiles.Normalize(runner.ApplicationProfile) == RunnerApplicationProfiles.MachineManagement)
        {
            if (machineResources?.IsValid() != true)
                throw new InvalidOperationException("Machine Action execution is paused because valid bounded container settings are unavailable.");
            request.ContainerPolicy = machineResources.ToActionContainerPolicy();
        }
        await runnerGateway.QueueExecutionAsync(runner.Id, userId ?? Guid.Empty, execution.Id, request, cancellationToken);
        return execution;
    }

    public async Task<IReadOnlyList<AppModuleExecution>> ListAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        await EnsureCanExecuteAsync(appId, userId, cancellationToken);
        return await db.AppModuleExecutions.AsNoTracking().Where(x => x.AppId == appId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
    }

    public async Task<AppActionModulesResponse> ListModulesAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        var (runnerId, deploymentTargetId) = await GetAuthorizedRunnerAsync(appId, userId, cancellationToken);
        return await runnerGateway.ListModulesAsync(runnerId, appId, deploymentTargetId, string.Empty, cancellationToken);
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
        var (runnerId, deploymentTargetId) = await GetAuthorizedRunnerAsync(appId, userId, cancellationToken);
        return await runnerGateway.ListVersionsAsync(runnerId, appId, deploymentTargetId, actionKey, cancellationToken);
    }

    public async Task<AppActionVersionOperationResponse> RestoreVersionAsync(Guid appId, string actionKey, string version, Guid userId, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        await EnsureCanUpdateAsync(appId, userId, cancellationToken);
        var (runnerId, deploymentTargetId) = await GetAuthorizedRunnerAsync(appId, userId, cancellationToken);
        return await runnerGateway.RestoreVersionAsync(runnerId, appId, deploymentTargetId, actionKey, version, cancellationToken);
    }

    public async Task<AppActionVersionOperationResponse> DeleteVersionAsync(Guid appId, string actionKey, string version, Guid userId, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        await EnsureCanUpdateAsync(appId, userId, cancellationToken);
        var (runnerId, deploymentTargetId) = await GetAuthorizedRunnerAsync(appId, userId, cancellationToken);
        return await runnerGateway.DeleteVersionAsync(runnerId, appId, deploymentTargetId, actionKey, version, cancellationToken);
    }

    public async Task<AppModuleExecution> GetAsync(Guid appId, Guid executionId, Guid userId, CancellationToken cancellationToken)
    {
        await EnsureCanExecuteAsync(appId, userId, cancellationToken);
        return await db.AppModuleExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.AppId == appId && x.Id == executionId, cancellationToken)
            ?? throw new KeyNotFoundException("Execution not found.");
    }

    public async Task<bool> CancelAsync(Guid appId, Guid executionId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanExecuteAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to cancel Actions in this App.");
        var execution = await GetAsync(appId, executionId, userId, cancellationToken);
        if (!execution.RunnerId.HasValue || execution.CompletedAtUtc.HasValue) return false;
        return await runnerGateway.CancelExecutionAsync(execution.RunnerId.Value, executionId, cancellationToken);
    }

    public async Task<AppModuleExecutionArtifactResponse> ReadArtifactAsync(Guid appId, Guid executionId, Guid userId, bool result, CancellationToken cancellationToken)
    {
        var execution = await GetAsync(appId, executionId, userId, cancellationToken);
        if (!execution.RunnerId.HasValue) return new(false, null, null, "Runner is unavailable.");
        if (!execution.DeploymentTargetId.HasValue)
            return new(false, null, null, "Execution has no deployment target.");
        var runnerId = execution.RunnerId.Value;
        return await runnerGateway.ReadExecutionArtifactAsync(
            runnerId,
            appId,
            execution.DeploymentTargetId.Value,
            execution.ModuleKey,
            executionId,
            result,
            cancellationToken);
    }

    private async Task<(Guid RunnerId, Guid DeploymentTargetId)> GetAuthorizedRunnerAsync(
        Guid appId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var target = await deploymentTargetResolver.ResolveForActionExecutionAsync(
            appId, targetSlug: null, userId, cancellationToken);
        var runner = target.ExecutionRunner!;
        EnsureAvailable(runner);
        return (runner.Id, target.Id);
    }

    private static void EnsureAvailable(Runner runner)
    {
        if (!runner.LastHeartbeatAtUtc.HasValue || runner.LastHeartbeatAtUtc < DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold)
            throw new InvalidOperationException("The assigned Runner is unavailable.");
    }

    private async Task EnsureCanUpdateAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        _ = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        if (!await authorizationService.CanUpdateAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to deploy or modify this App.");
    }

    private async Task EnsureCanExecuteAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await authorizationService.CanExecuteAppAsync(appId, userId, cancellationToken))
        {
            throw new KeyNotFoundException("App not found.");
        }
    }

}
