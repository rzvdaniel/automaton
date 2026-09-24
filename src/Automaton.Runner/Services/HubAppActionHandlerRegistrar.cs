using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace Automaton.Runner.Console.Services;

internal sealed class HubAppActionHandlerRegistrar(
    RunnerDeploymentService runnerDeploymentService,
    AppActionExecutionService appActionExecutionService,
    AppActionDeploymentService actionDeploymentService)
{
    private const string DeployAppPackageMethod = "DeployAppPackage";
    private const string ExecuteAppActionModuleMethod = "ExecuteAppActionModule";
    private const string CancelAppModuleExecutionMethod = "CancelAppModuleExecution";
    private const string ReadAppModuleExecutionLogsMethod = "ReadAppModuleExecutionLogs";
    private const string ReadAppModuleExecutionResultMethod = "ReadAppModuleExecutionResult";
    private const string ListAppActionModulesMethod = "ListAppActionModules";
    private const string ListAppActionVersionsMethod = "ListAppActionVersions";
    private const string RestoreAppActionVersionMethod = "RestoreAppActionVersion";
    private const string DeleteAppActionVersionMethod = "DeleteAppActionVersion";
    private const string PrepareAppActionModuleMethod = "PrepareAppActionModule";
    private const string SynchronizeAppActionModulesMethod = "SynchronizeAppActionModules";
    private const string CleanupAppDeploymentTargetMethod = "CleanupAppDeploymentTarget";

    public void Register(
        HubConnection connection,
        Func<Func<Task<DeployAppPackageResult>>, Task<DeployAppPackageResult>> executeWithActivity,
        Func<CleanupAppDeploymentTargetRequest, Task<CleanupAppDeploymentTargetResult>> cleanupTarget)
    {
        connection.On<DeployAppPackageRequest, DeployAppPackageResult>(DeployAppPackageMethod, request =>
            executeWithActivity(() => runnerDeploymentService.DeployPackage(request)));
        connection.On<ExecuteAppActionModuleRequest, AppActionExecutionResult>(ExecuteAppActionModuleMethod, request =>
            appActionExecutionService.ExecuteAsync(request));
        connection.On<CancelAppModuleExecutionRequest, bool>(CancelAppModuleExecutionMethod, request =>
            Task.FromResult(appActionExecutionService.Cancel(request.ExecutionId)));
        connection.On<ReadAppModuleExecutionArtifactRequest, AppModuleExecutionArtifactResponse>(ReadAppModuleExecutionLogsMethod, request =>
            Task.FromResult(appActionExecutionService.ReadLogs(request)));
        connection.On<ReadAppModuleExecutionArtifactRequest, AppModuleExecutionArtifactResponse>(ReadAppModuleExecutionResultMethod, request =>
            Task.FromResult(appActionExecutionService.ReadResult(request)));
        connection.On<AppActionModuleRequest, AppActionModulesResponse>(ListAppActionModulesMethod, request =>
            Task.FromResult(actionDeploymentService.ListModules(request.AppId, request.DeploymentTargetId)));
        connection.On<AppActionModuleRequest, AppActionVersionsResponse>(ListAppActionVersionsMethod, request =>
            Task.FromResult(actionDeploymentService.ListVersions(request.AppId, request.DeploymentTargetId, request.ActionKey)));
        connection.On<AppActionVersionRequest, AppActionVersionOperationResponse>(RestoreAppActionVersionMethod, request =>
            Task.FromResult(actionDeploymentService.RestoreVersion(
                request.AppId, request.DeploymentTargetId, request.ActionKey, request.Version)));
        connection.On<AppActionVersionRequest, AppActionVersionOperationResponse>(DeleteAppActionVersionMethod, request =>
            Task.FromResult(actionDeploymentService.DeleteVersion(
                request.AppId, request.DeploymentTargetId, request.ActionKey, request.Version)));
        connection.On<PrepareAppActionModuleRequest, AppActionDeploymentResult>(PrepareAppActionModuleMethod, request =>
            actionDeploymentService.PrepareModuleAsync(request, CancellationToken.None));
        connection.On<SynchronizeAppActionModulesRequest, int>(SynchronizeAppActionModulesMethod, request =>
            Task.FromResult(actionDeploymentService.SynchronizeModules(request)));
        connection.On<CleanupAppDeploymentTargetRequest, CleanupAppDeploymentTargetResult>(
            CleanupAppDeploymentTargetMethod, cleanupTarget);
    }
}
