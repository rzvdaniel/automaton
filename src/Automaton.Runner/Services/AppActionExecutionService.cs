using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Core.Enums;

namespace Automaton.Runner.Console.Services;

public sealed class AppActionExecutionService
{
    private static readonly TimeSpan ArtifactRetention = TimeSpan.FromHours(24);
    private readonly RunnerWorkspaceService workspace;
    private readonly AppActionExecutionCoordinator coordinator;
    private readonly MachineActionExecutionHandler? machineActionExecution;
    private readonly ConcurrentDictionary<Guid, ActiveExecution> active = new();
    internal const ushort MaximumConcurrentExecutions = 4;
    private readonly SemaphoreSlim concurrencyGate = new(MaximumConcurrentExecutions, MaximumConcurrentExecutions);
    private bool IsMachineManagement => RunnerApplicationProfiles.Normalize(
        Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE")) == RunnerApplicationProfiles.MachineManagement;

    public AppActionExecutionService(
        RunnerWorkspaceService workspace,
        AppActionExecutionCoordinator? coordinator = null,
        MachineActionExecutionHandler? machineActionExecution = null)
    {
        this.workspace = workspace;
        this.coordinator = coordinator ?? new AppActionExecutionCoordinator();
        this.machineActionExecution = machineActionExecution;
    }

    public async Task<AppActionExecutionResult> ExecuteAsync(ExecuteAppActionModuleRequest request, CancellationToken cancellationToken = default)
    {
        var machineManagement = IsMachineManagement;
        if (machineManagement && machineActionExecution is null)
            throw new UnauthorizedAccessException("Isolated Action execution is unavailable on this Machine Management Runner.");

        if (active.TryGetValue(request.ExecutionId, out var existingExecution))
            return await existingExecution.Completion.Task.WaitAsync(cancellationToken);

        await concurrencyGate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteUnderConcurrencyGateAsync(request, machineManagement, cancellationToken);
        }
        finally
        {
            concurrencyGate.Release();
        }
    }

    private async Task<AppActionExecutionResult> ExecuteUnderConcurrencyGateAsync(
        ExecuteAppActionModuleRequest request,
        bool machineManagement,
        CancellationToken cancellationToken)
    {
        using var actionLock = await coordinator.EnterAsync(request.AppId, request.DeploymentTargetId, request.ActionKey, cancellationToken);
        if (active.TryGetValue(request.ExecutionId, out var existing))
            return await existing.Completion.Task.WaitAsync(cancellationToken);
        AppActionManifestParser.ValidateKey(request.ActionKey);
        var statePath = Path.Combine(workspace.GetAppActionExecutionsDirectory(request.AppId, request.DeploymentTargetId, request.ActionKey),
            request.ExecutionId.ToString("N"), ".automaton-execution.json");
        var preparation = AppActionExecutionPreparer.Prepare(request, workspace);
        if (preparation.ImmediateResult is not null)
        {
            await PersistImmediateResultAsync(request, machineManagement, statePath, preparation.ImmediateResult);
            return preparation.ImmediateResult;
        }

        var prepared = preparation.Execution!;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(prepared.Timeout);
        var execution = new ActiveExecution(linked);
        if (!active.TryAdd(request.ExecutionId, execution))
            return await ResolveDuplicateDispatchAsync(request, prepared, cancellationToken);
        WriteExecutionState(statePath, CreateRunningState(request, prepared));
        return await RunPreparedExecutionAsync(request, machineManagement, prepared, statePath, execution, cancellationToken);
    }

    private async Task PersistImmediateResultAsync(
        ExecuteAppActionModuleRequest request,
        bool machineManagement,
        string statePath,
        AppActionExecutionResult result)
    {
        if (!machineManagement || result.Status != AppModuleExecutionStatuses.Lost) return;
        await machineActionExecution!.RemoveAsync(request);
        WriteExecutionState(statePath, CreateTerminalState(request, result));
    }

    private async Task<AppActionExecutionResult> ResolveDuplicateDispatchAsync(
        ExecuteAppActionModuleRequest request,
        AppActionPreparedExecution prepared,
        CancellationToken cancellationToken)
    {
        if (active.TryGetValue(request.ExecutionId, out var existing))
            return await existing.Completion.Task.WaitAsync(cancellationToken);
        return AppActionExecutionResultFactory.Create(request.ExecutionId, AppModuleExecutionStatuses.Lost,
            null, AppModuleExecutionErrorCodes.AmbiguousDispatch,
            "Runner execution ownership changed before acceptance could be confirmed.", prepared.DeploymentHash,
            prepared.StartedAtUtc, logs: false, result: false);
    }

    private static RunnerActionExecutionState CreateRunningState(
        ExecuteAppActionModuleRequest request,
        AppActionPreparedExecution prepared)
        => new(false, new AppActionExecutionResult
        {
            ExecutionId = request.ExecutionId,
            Status = AppModuleExecutionStatuses.Running,
            DeploymentHash = prepared.DeploymentHash,
            StartedAtUtc = prepared.StartedAtUtc
        }, request.AppId, request.DeploymentTargetId, request.ActionKey);

    private async Task<AppActionExecutionResult> RunPreparedExecutionAsync(
        ExecuteAppActionModuleRequest request,
        bool machineManagement,
        AppActionPreparedExecution prepared,
        string statePath,
        ActiveExecution execution,
        CancellationToken cancellationToken)
    {
        AppActionExecutionResult Finish(AppActionExecutionResult result)
        {
            WriteExecutionState(statePath, CreateTerminalState(request, result));
            execution.Completion.TrySetResult(result);
            return result;
        }

        try
        {
            var result = machineManagement
                ? await machineActionExecution!.ExecuteAsync(request, prepared.Manifest, prepared.Input, prepared.LogsPath,
                    prepared.ResultPath, prepared.DeploymentHash, prepared.StartedAtUtc, cancellationToken,
                    execution.Source.Token, () => execution.ManualCancellationRequested)
                : await AppActionProcessExecutor.ExecuteAsync(request, prepared.Manifest, prepared.CurrentDirectory,
                    prepared.Input, prepared.LogsPath, prepared.ResultPath, prepared.DeploymentHash, prepared.StartedAtUtc,
                    cancellationToken, execution.Source.Token, () => execution.ManualCancellationRequested);
            return Finish(result);
        }
        catch (Win32Exception exception)
        {
            return Finish(AppActionExecutionResultFactory.Create(request.ExecutionId, AppModuleExecutionStatuses.Failed, null,
                AppModuleExecutionErrorCodes.RuntimeUnavailable, Limit(exception.Message, 1000), prepared.DeploymentHash,
                prepared.StartedAtUtc, false, false));
        }
        finally
        {
            if (!execution.Completion.Task.IsCompleted)
            {
                var interrupted = AppActionExecutionResultFactory.Create(request.ExecutionId,
                    AppModuleExecutionStatuses.Lost, null, AppModuleExecutionErrorCodes.AmbiguousDispatch,
                    "Runner execution ended without a confirmed result.", prepared.DeploymentHash,
                    prepared.StartedAtUtc, logs: false, result: false);
                WriteExecutionState(statePath, CreateTerminalState(request, interrupted));
                execution.Completion.TrySetResult(interrupted);
            }
            active.TryRemove(request.ExecutionId, out _);
        }
    }

    public bool Cancel(Guid executionId)
    {
        if (!active.TryGetValue(executionId, out var execution)) return false;
        execution.ManualCancellationRequested = true;
        execution.Source.Cancel();
        return true;
    }

    public int CleanupExpiredArtifacts(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (!Directory.Exists(workspace.AppsRoot)) return 0;
        var removed = 0;
        foreach (var executionsDirectory in Directory.GetDirectories(workspace.AppsRoot, "executions", SearchOption.AllDirectories))
        {
            var normalized = executionsDirectory.Replace('\\', '/');
            if (!normalized.Contains("/artifacts/current/actions/", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var directory in Directory.GetDirectories(executionsDirectory))
            {
                if (Directory.GetLastWriteTimeUtc(directory) + ArtifactRetention > now) continue;
                Directory.Delete(directory, recursive: true);
                removed++;
            }
        }
        return removed;
    }

    public async Task CleanupInterruptedActionContainersAsync(CancellationToken cancellationToken)
    {
        if (RunnerApplicationProfiles.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"))
            != RunnerApplicationProfiles.MachineManagement || machineActionExecution is null || !Directory.Exists(workspace.AppsRoot))
            return;

        foreach (var executionsDirectory in Directory.GetDirectories(workspace.AppsRoot, "executions", SearchOption.AllDirectories))
        foreach (var executionDirectory in Directory.GetDirectories(executionsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var statePath = Path.Combine(executionDirectory, ".automaton-execution.json");
            var state = ReadExecutionState(statePath);
            if (state is null || state.Terminal || state.Result.ExecutionId == Guid.Empty
                || state.AppId == Guid.Empty || state.DeploymentTargetId == Guid.Empty
                || string.IsNullOrWhiteSpace(state.ActionKey))
                continue;
            await machineActionExecution.RemoveAsync(new ExecuteAppActionModuleRequest
            {
                ExecutionId = state.Result.ExecutionId,
                AppId = state.AppId,
                DeploymentTargetId = state.DeploymentTargetId,
                ActionKey = state.ActionKey
            });
            var lost = AppActionExecutionResultFactory.Create(state.Result.ExecutionId,
                AppModuleExecutionStatuses.Lost, null, AppModuleExecutionErrorCodes.AmbiguousDispatch,
                "Runner restarted before completion was confirmed; the Action was not replayed.",
                state.Result.DeploymentHash, state.Result.StartedAtUtc, logs: false, result: false);
            WriteExecutionState(statePath, CreateTerminalState(new ExecuteAppActionModuleRequest
            {
                ExecutionId = state.Result.ExecutionId, AppId = state.AppId,
                DeploymentTargetId = state.DeploymentTargetId, ActionKey = state.ActionKey
            }, lost));
        }
    }

    public AppModuleExecutionArtifactResponse ReadLogs(ReadAppModuleExecutionArtifactRequest request)
        => ReadArtifact(request, "stderr.log");

    public AppModuleExecutionArtifactResponse ReadResult(ReadAppModuleExecutionArtifactRequest request)
        => ReadArtifact(request, "result.json");

    public AppActionExecutionResult? ReadPersistedResult(ReadAppModuleExecutionArtifactRequest request)
    {
        var statePath = Path.Combine(
            workspace.GetAppActionExecutionsDirectory(request.AppId, request.DeploymentTargetId, request.ActionKey),
            request.ExecutionId.ToString("N"), ".automaton-execution.json");
        return ReadExecutionState(statePath)?.Result;
    }

    private AppModuleExecutionArtifactResponse ReadArtifact(ReadAppModuleExecutionArtifactRequest request, string fileName)
    {
        AppActionManifestParser.ValidateKey(request.ActionKey);
        var path = Path.Combine(
            workspace.GetAppActionExecutionsDirectory(
                request.AppId,
                request.DeploymentTargetId,
                request.ActionKey),
            request.ExecutionId.ToString("N"),
            fileName);
        if (!File.Exists(path)) return new(false, null, null, "Artifact is not available.");
        var expires = File.GetLastWriteTimeUtc(path) + ArtifactRetention;
        if (expires <= DateTime.UtcNow) return new(false, null, expires, "Artifact has expired.");
        return new(true, File.ReadAllText(path), expires);
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];

    private static RunnerActionExecutionState? ReadExecutionState(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<RunnerActionExecutionState>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return new RunnerActionExecutionState(false, new AppActionExecutionResult());
        }
    }

    private static void WriteExecutionState(string path, RunnerActionExecutionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private sealed class ActiveExecution(CancellationTokenSource source)
    {
        public CancellationTokenSource Source { get; } = source;
        public TaskCompletionSource<AppActionExecutionResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool ManualCancellationRequested;
    }

    private static RunnerActionExecutionState CreateTerminalState(
        ExecuteAppActionModuleRequest request,
        AppActionExecutionResult result)
        => new(true, result, request.AppId, request.DeploymentTargetId, request.ActionKey);

    private sealed record RunnerActionExecutionState(
        bool Terminal,
        AppActionExecutionResult Result,
        Guid AppId = default,
        Guid DeploymentTargetId = default,
        string ActionKey = "");
}
