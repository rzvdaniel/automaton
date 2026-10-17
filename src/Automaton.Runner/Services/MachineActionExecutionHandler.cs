using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public sealed class MachineActionExecutionHandler(HostContainerExecutionService execution)
{
    public async Task<AppActionExecutionResult> ExecuteAsync(
        ExecuteAppActionModuleRequest request,
        AppActionManifest manifest,
        string input,
        string logsPath,
        string resultPath,
        string? deploymentHash,
        DateTime startedAt,
        CancellationToken callerToken,
        CancellationToken executionToken,
        Func<bool> cancellationRequested)
    {
        var policy = request.ContainerPolicy;
        if (policy?.IsValid() != true)
            return Failed(request, "Machine Action container policy is unavailable.", deploymentHash, startedAt);

        HostContainerExecutionResponse response;
        try
        {
            response = await execution.ExecuteAsync(CreateRequest(request, manifest, input, policy), executionToken);
        }
        catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
        {
            return await CompleteCancellationAsync(request, logsPath, deploymentHash, startedAt,
                callerToken.IsCancellationRequested || cancellationRequested(), string.Empty);
        }

        if (callerToken.IsCancellationRequested || cancellationRequested())
            return await CompleteCancellationAsync(request, logsPath, deploymentHash, startedAt, true, response.Logs);
        if (response.TimedOut)
            return await CompleteCancellationAsync(request, logsPath, deploymentHash, startedAt, false, response.Logs);
        if (!response.Success && response.ErrorCode != "action_exit")
            return AppActionExecutionResultFactory.Create(request.ExecutionId,
                AppModuleExecutionStatuses.Failed, response.ExitCode, AppModuleExecutionErrorCodes.RuntimeUnavailable,
                Limit(response.Error, 1000), deploymentHash, startedAt, false, false);

        return await AppActionProcessExecutor.CompleteContainerOutputAsync(
            request, manifest, response.Output, response.Logs, response.OutputExceededLimit,
            response.ExitCode ?? (response.Success ? 0 : 1), logsPath, resultPath, deploymentHash, startedAt);
    }

    public async Task RemoveAsync(ExecuteAppActionModuleRequest request)
    {
        try
        {
            await execution.ExecuteAsync(new HostContainerExecutionRequest
            {
                OperationId = Guid.NewGuid(),
                Scope = MachineActionExecutionPolicy.CreateScope(request.AppId, request.DeploymentTargetId),
                Operation = HostContainerOperationKinds.RemoveActionContainer,
                ActionKey = request.ActionKey,
                ActionExecutionId = request.ExecutionId
            }, CancellationToken.None);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(exception, "Could not remove an interrupted Action container for {ExecutionId}", request.ExecutionId);
        }
    }

    private static HostContainerExecutionRequest CreateRequest(
        ExecuteAppActionModuleRequest request,
        AppActionManifest manifest,
        string input,
        ActionContainerPolicy policy)
    {
        var timeout = Math.Min(request.TimeoutSeconds ?? manifest.Spec.TimeoutSeconds, manifest.Spec.TimeoutSeconds);
        return new HostContainerExecutionRequest
        {
            OperationId = request.ExecutionId,
            Scope = MachineActionExecutionPolicy.CreateScope(request.AppId, request.DeploymentTargetId),
            Operation = HostContainerOperationKinds.ExecuteAction,
            RequestedImage = policy.RuntimeImage,
            ActionKey = request.ActionKey,
            ActionExecutionId = request.ExecutionId,
            ActionInputJson = input,
            TimeoutSeconds = timeout,
            ContainerLimits = policy.Limits,
            SharedMemoryMb = policy.SharedMemoryMb,
            DockerLogMaxSizeMb = policy.DockerLogMaxSizeMb,
            DockerLogMaxFiles = policy.DockerLogMaxFiles,
            Resources = new(HostContainerResourceKinds.Source, HostContainerResourceKinds.Artifact,
                HostContainerResourceKinds.ApplicationData),
            Environment = new Dictionary<string, string>(request.Secrets, StringComparer.Ordinal)
        };
    }

    private static async Task<AppActionExecutionResult> CompleteCancellationAsync(
        ExecuteAppActionModuleRequest request,
        string logsPath,
        string? deploymentHash,
        DateTime startedAt,
        bool cancelled,
        string logs)
    {
        await AppActionProcessExecutor.WriteRedactedLogsAsync(request, logsPath, logs, CancellationToken.None);
        var status = cancelled ? AppModuleExecutionStatuses.Cancelled : AppModuleExecutionStatuses.TimedOut;
        var error = cancelled ? AppModuleExecutionErrorCodes.Cancelled : AppModuleExecutionErrorCodes.TimedOut;
        return AppActionExecutionResultFactory.Create(request.ExecutionId, status, null, error, error,
            deploymentHash, startedAt, logs: true, result: false);
    }

    private static AppActionExecutionResult Failed(
        ExecuteAppActionModuleRequest request,
        string summary,
        string? deploymentHash,
        DateTime startedAt)
        => AppActionExecutionResultFactory.Create(request.ExecutionId,
            AppModuleExecutionStatuses.Failed, null, AppModuleExecutionErrorCodes.RuntimeUnavailable,
            summary, deploymentHash, startedAt, false, false);

    private static string Limit(string? value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value[..max];
}
