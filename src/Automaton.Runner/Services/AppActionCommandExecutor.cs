using Automaton.Core.Models;
using System.Text.Json;

namespace Automaton.Runner.Console.Services;

/// <summary>Runs the existing Action owners behind journaled, authenticated commands.</summary>
internal sealed class AppActionCommandExecutor(AppActionExecutionService execution, AppActionDeploymentService deployment,
    RunnerDeploymentService runtime)
{
    public async Task<RunnerExecutionResult> ExecuteAsync(AppActionCommand command, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var started = DateTime.UtcNow;
        object response = command.Kind switch
        {
            "execute" => await execution.ExecuteAsync(command.Read<ExecuteAppActionModuleRequest>(), token),
            "list-modules" => ListModules(command.Read<AppActionModuleRequest>()),
            "list-versions" => ListVersions(command.Read<AppActionModuleRequest>()),
            "restore-version" => Restore(command.Read<AppActionVersionRequest>()),
            "delete-version" => Delete(command.Read<AppActionVersionRequest>()),
            "read-logs" => execution.ReadLogs(command.Read<ReadAppModuleExecutionArtifactRequest>()),
            "read-result" => execution.ReadResult(command.Read<ReadAppModuleExecutionArtifactRequest>()),
            "prepare-module" => await deployment.PrepareModuleAsync(command.Read<PrepareAppActionModuleRequest>(), token),
            "cleanup-target" => await CleanupAsync(command.Read<CleanupAppDeploymentTargetRequest>() with { DeleteApplicationStorage = false }),
            "cleanup-app" => await CleanupAsync(command.Read<CleanupAppDeploymentTargetRequest>() with { DeleteApplicationStorage = true }),
            _ => throw new InvalidDataException("Unknown App Action command.")
        };
        return new() { Outcome = "Succeeded", Started = started, Finished = DateTime.UtcNow,
            ActionResult = JsonSerializer.SerializeToElement(response) };
    }
    private AppActionModulesResponse ListModules(AppActionModuleRequest request)
        => deployment.ListModules(request.AppId, request.DeploymentTargetId);
    private AppActionVersionsResponse ListVersions(AppActionModuleRequest request)
        => deployment.ListVersions(request.AppId, request.DeploymentTargetId, request.ActionKey);
    private AppActionVersionOperationResponse Restore(AppActionVersionRequest request)
        => deployment.RestoreVersion(request.AppId, request.DeploymentTargetId, request.ActionKey, request.Version);
    private AppActionVersionOperationResponse Delete(AppActionVersionRequest request)
        => deployment.DeleteVersion(request.AppId, request.DeploymentTargetId, request.ActionKey, request.Version);
    private async Task<CleanupAppDeploymentTargetResult> CleanupAsync(CleanupAppDeploymentTargetRequest request)
    {
        await runtime.CleanupTargetAsync(request.AppId, request.DeploymentTargetId, request.DeleteApplicationStorage);
        return new(request.AppId, request.DeploymentTargetId, true);
    }
}
