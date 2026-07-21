using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Services;

public static class AppModuleTriggerTypes
{
    public const string Manual = "Manual";
    public const string Internal = "Internal";
}

public sealed class AppActionExecutionService
{
    private readonly ApplicationDbContext db;
    private readonly AppsService appsService;
    private readonly AppFileStorageService fileStorageService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly IHubContext<AutomatonHub> hub;
    private readonly IServiceScopeFactory scopeFactory;

    public AppActionExecutionService(ApplicationDbContext db, AppsService appsService, AppFileStorageService fileStorageService, ResourceAuthorizationService authorizationService, IHubContext<AutomatonHub> hub, IServiceScopeFactory scopeFactory)
    {
        this.db = db;
        this.appsService = appsService;
        this.fileStorageService = fileStorageService;
        this.authorizationService = authorizationService;
        this.hub = hub;
        this.scopeFactory = scopeFactory;
    }

    public async Task<AppModuleExecution> ExecuteAsync(Guid appId, string actionKey, Guid userId, JsonElement input, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        if (!await authorizationService.CanExecuteAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to execute Actions in this App.");
        if (!app.ExecutionRunnerId.HasValue)
            throw new InvalidOperationException("No Runner is assigned to this App.");

        var runner = await db.Runners.AsNoTracking().SingleOrDefaultAsync(r => r.Id == app.ExecutionRunnerId.Value, cancellationToken);
        if (runner == null || string.IsNullOrWhiteSpace(runner.ConnectionId))
            throw new InvalidOperationException("The assigned Runner is unavailable.");

        var timeoutSeconds = ResolveActionTimeoutSeconds(app.OwnerId ?? userId, appId, actionKey);
        var execution = new AppModuleExecution
        {
            Id = Guid.NewGuid(), AppId = appId, ModuleType = AppModuleTypes.Action, ModuleKey = actionKey,
            RunnerId = runner.Id, TriggerType = AppModuleTriggerTypes.Manual, RequestedByUserId = userId,
            Status = AppModuleExecutionStatuses.Dispatching, CreatedAtUtc = DateTime.UtcNow,
            ExecutionDeadlineUtc = DateTime.UtcNow.AddSeconds(timeoutSeconds + 300),
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
                await scope.ServiceProvider.GetRequiredService<AppActionExecutionService>()
                    .DispatchAsync(executionId, connectionId, inputJson);
            }
            catch
            {
                // The hosted reconciler marks the persisted record Lost if dispatch
                // fails before a terminal result is saved.
            }
        });
        return execution;
    }

    private int ResolveActionTimeoutSeconds(Guid ownerId, Guid appId, string actionKey)
    {
        try
        {
            var manifestPath = Path.Combine(fileStorageService.GetAppSourceDirectoryPath(ownerId, appId), "actions", actionKey, "action.yaml");
            return File.Exists(manifestPath)
                ? AppActionManifestParser.ParseFile(manifestPath, actionKey).Spec.TimeoutSeconds
                : AppActionManifestParser.MaximumTimeoutSeconds;
        }
        catch
        {
            return AppActionManifestParser.MaximumTimeoutSeconds;
        }
    }

    internal async Task DispatchAsync(Guid executionId, string connectionId, string inputJson)
    {
        var execution = await db.AppModuleExecutions.SingleOrDefaultAsync(item => item.Id == executionId);
        if (execution == null) return;
        try
        {
            execution.Status = AppModuleExecutionStatuses.Running;
            execution.StartedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            using var inputDocument = JsonDocument.Parse(inputJson);
            var result = await hub.Clients.Client(connectionId).InvokeAsync<AppActionExecutionResult>(
                AutomatonHubMethods.ExecuteAppActionModule,
                new ExecuteAppActionModuleRequest
                {
                    ExecutionId = execution.Id, AppId = execution.AppId, ActionKey = execution.ModuleKey,
                    Input = inputDocument.RootElement.Clone()
                }, CancellationToken.None);
            ApplyResult(execution, result);
        }
        catch (Exception ex)
        {
            execution.Status = AppModuleExecutionStatuses.Failed;
            execution.ErrorCode = AppModuleExecutionErrorCodes.PlatformError;
            execution.FailureSummary = Limit(ex.Message, 1000);
            execution.CompletedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(CancellationToken.None);
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
        var connectionId = await GetAuthorizedRunnerConnectionAsync(appId, userId, cancellationToken);
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionModulesResponse>(
            AutomatonHubMethods.ListAppActionModules, appId, cancellationToken);
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
        var connectionId = await GetAuthorizedRunnerConnectionAsync(appId, userId, cancellationToken);
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionsResponse>(
            AutomatonHubMethods.ListAppActionVersions, new AppActionModuleRequest(appId, actionKey), cancellationToken);
    }

    public async Task<AppActionVersionOperationResponse> RestoreVersionAsync(Guid appId, string actionKey, string version, Guid userId, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        await EnsureCanUpdateAsync(appId, userId, cancellationToken);
        var connectionId = await GetAuthorizedRunnerConnectionAsync(appId, userId, cancellationToken);
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionOperationResponse>(
            AutomatonHubMethods.RestoreAppActionVersion, new AppActionVersionRequest(appId, actionKey, version), cancellationToken);
    }

    public async Task<AppActionVersionOperationResponse> DeleteVersionAsync(Guid appId, string actionKey, string version, Guid userId, CancellationToken cancellationToken)
    {
        AppActionManifestParser.ValidateKey(actionKey);
        await EnsureCanUpdateAsync(appId, userId, cancellationToken);
        var connectionId = await GetAuthorizedRunnerConnectionAsync(appId, userId, cancellationToken);
        return await hub.Clients.Client(connectionId).InvokeAsync<AppActionVersionOperationResponse>(
            AutomatonHubMethods.DeleteAppActionVersion, new AppActionVersionRequest(appId, actionKey, version), cancellationToken);
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
        var connectionId = await db.Runners.Where(x => x.Id == execution.RunnerId).Select(x => x.ConnectionId).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(connectionId)) return new(false, null, null, "Runner is unavailable.");
        return await hub.Clients.Client(connectionId).InvokeAsync<AppModuleExecutionArtifactResponse>(
            result ? AutomatonHubMethods.ReadAppModuleExecutionResult : AutomatonHubMethods.ReadAppModuleExecutionLogs,
            new ReadAppModuleExecutionArtifactRequest(appId, execution.ModuleKey, executionId), cancellationToken);
    }

    private static void ApplyResult(AppModuleExecution execution, AppActionExecutionResult result)
    {
        execution.Status = result.Status; execution.DeploymentHash = result.DeploymentHash; execution.ExitCode = result.ExitCode;
        execution.ErrorCode = result.ErrorCode; execution.FailureSummary = Limit(result.FailureSummary, 1000);
        execution.StartedAtUtc = result.StartedAtUtc; execution.CompletedAtUtc = result.CompletedAtUtc;
        execution.LogsAvailableUntilUtc = result.LogsExpireAtUtc; execution.ResultAvailableUntilUtc = result.ResultExpireAtUtc;
    }

    private async Task<string> GetAuthorizedRunnerConnectionAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        if (!app.ExecutionRunnerId.HasValue) throw new InvalidOperationException("No Runner is assigned to this App.");
        var connectionId = await db.Runners.AsNoTracking().Where(runner => runner.Id == app.ExecutionRunnerId.Value)
            .Select(runner => runner.ConnectionId).SingleOrDefaultAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(connectionId) ? connectionId : throw new InvalidOperationException("The assigned Runner is unavailable.");
    }

    private async Task EnsureCanUpdateAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        _ = await appsService.GetAccessibleAppInfoByIdAsync(appId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("App not found.");
        if (!await authorizationService.CanUpdateAppAsync(appId, userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not have permission to deploy or modify this App.");
    }

    private static string? Limit(string? value, int max) => value?.Length > max ? value[..max] : value;
}
