using System.Text.Json;
using Automaton.Core.Enums;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class AppActionExecutionPreparer
{
    public static void EnsureProfileAllowsExecution()
    {
        if (RunnerApplicationProfiles.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"))
            == RunnerApplicationProfiles.MachineManagement)
            throw new UnauthorizedAccessException("Action execution is disabled for Machine Management Runners until isolated job execution is available.");
    }

    public static AppActionExecutionPreparation Prepare(
        ExecuteAppActionModuleRequest request,
        RunnerWorkspaceService workspace)
    {
        var executionDirectory = Path.Combine(
            workspace.GetAppActionExecutionsDirectory(request.AppId, request.DeploymentTargetId, request.ActionKey),
            request.ExecutionId.ToString("N"));
        var statePath = Path.Combine(executionDirectory, ".automaton-execution.json");
        var priorResult = ReadPriorResult(statePath, request.ExecutionId);
        if (priorResult is not null) return new(null, priorResult);

        var currentDirectory = workspace.GetAppActionCurrentDirectory(request.AppId, request.DeploymentTargetId, request.ActionKey);
        var manifestPath = Path.Combine(currentDirectory, "action.yaml");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Action '{request.ActionKey}' is not deployed.");

        var allowPrivileged = request.SystemInfrastructure
            && RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"))
                == RunnerPurposes.Infrastructure;
        var manifest = AppActionManifestParser.ParseFile(
            manifestPath,
            request.ActionKey,
            allowPrivilegedCapabilities: allowPrivileged);
        var deploymentHash = ReadDeploymentHash(Path.Combine(currentDirectory, ".automaton-action-deployment.json"));
        if (!string.IsNullOrWhiteSpace(request.ExpectedDeploymentHash)
            && !string.Equals(request.ExpectedDeploymentHash, deploymentHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The deployed Action hash does not match the requested hash.");

        var input = request.Input.ValueKind == JsonValueKind.Undefined ? "null" : request.Input.GetRawText();
        var startedAt = DateTime.UtcNow;
        var validationError = AppActionExecutionInputValidator.Validate(request.Input, input, manifest.Spec.Input);
        if (validationError is not null)
        {
            var invalidInput = AppActionExecutionResultFactory.Create(
                request.ExecutionId,
                AppModuleExecutionStatuses.Failed,
                default,
                AppModuleExecutionErrorCodes.InvalidInput,
                validationError,
                deploymentHash,
                startedAt,
                logs: false,
                result: false);
            return new(null, invalidInput);
        }

        Directory.CreateDirectory(executionDirectory);
        return new(new AppActionPreparedExecution(
            manifest,
            currentDirectory,
            input,
            Path.Combine(executionDirectory, "stderr.log"),
            Path.Combine(executionDirectory, "result.json"),
            statePath,
            deploymentHash,
            startedAt,
            TimeSpan.FromSeconds(Math.Min(request.TimeoutSeconds ?? manifest.Spec.TimeoutSeconds, manifest.Spec.TimeoutSeconds))), null);
    }

    private static AppActionExecutionResult? ReadPriorResult(string statePath, Guid executionId)
    {
        if (!File.Exists(statePath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<PersistedExecutionState>(File.ReadAllText(statePath));
            if (state is null) return null;
            if (state.Terminal) return state.Result;
            return AppActionExecutionResultFactory.Create(
                executionId,
                AppModuleExecutionStatuses.Lost,
                null,
                AppModuleExecutionErrorCodes.AmbiguousDispatch,
                "Runner acceptance was recorded, but completion was not confirmed after interruption.",
                state.Result.DeploymentHash,
                state.Result.StartedAtUtc,
                logs: false,
                result: false);
        }
        catch (JsonException)
        {
            return AppActionExecutionResultFactory.Create(
                executionId,
                AppModuleExecutionStatuses.Lost,
                null,
                AppModuleExecutionErrorCodes.AmbiguousDispatch,
                "Runner acceptance was recorded, but completion was not confirmed after interruption.",
                null,
                default,
                logs: false,
                result: false);
        }
    }

    private static string? ReadDeploymentHash(string path)
    {
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("contentHash", out var value) ? value.GetString() : null;
    }

    private sealed record PersistedExecutionState(bool Terminal, AppActionExecutionResult Result);
}

internal sealed record AppActionExecutionPreparation(
    AppActionPreparedExecution? Execution,
    AppActionExecutionResult? ImmediateResult);

internal sealed record AppActionPreparedExecution(
    AppActionManifest Manifest,
    string CurrentDirectory,
    string Input,
    string LogsPath,
    string ResultPath,
    string StatePath,
    string? DeploymentHash,
    DateTime StartedAtUtc,
    TimeSpan Timeout);
