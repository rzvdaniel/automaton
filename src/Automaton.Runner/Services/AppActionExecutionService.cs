using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public sealed class AppActionExecutionService
{
    private static readonly TimeSpan ArtifactRetention = TimeSpan.FromHours(24);
    private readonly RunnerWorkspaceService workspace;
    private readonly AppActionExecutionCoordinator coordinator;
    private readonly ConcurrentDictionary<Guid, ActiveExecution> active = new();
    internal const ushort MaximumConcurrentExecutions = 4;
    private readonly SemaphoreSlim concurrencyGate = new(MaximumConcurrentExecutions, MaximumConcurrentExecutions);

    public AppActionExecutionService(RunnerWorkspaceService workspace, AppActionExecutionCoordinator? coordinator = null)
    {
        this.workspace = workspace;
        this.coordinator = coordinator ?? new AppActionExecutionCoordinator();
    }

    public async Task<AppActionExecutionResult> ExecuteAsync(ExecuteAppActionModuleRequest request, CancellationToken cancellationToken = default)
    {
        AppActionExecutionPreparer.EnsureProfileAllowsExecution();

        if (active.TryGetValue(request.ExecutionId, out var existingExecution))
            return await existingExecution.Completion.Task.WaitAsync(cancellationToken);

        await concurrencyGate.WaitAsync(cancellationToken);
        try
        {
        using var actionLock = await coordinator.EnterAsync(
            request.AppId,
            request.DeploymentTargetId,
            request.ActionKey,
            cancellationToken);
        if (active.TryGetValue(request.ExecutionId, out existingExecution))
            return await existingExecution.Completion.Task.WaitAsync(cancellationToken);
        AppActionManifestParser.ValidateKey(request.ActionKey);
        var preparation = AppActionExecutionPreparer.Prepare(request, workspace);
        if (preparation.ImmediateResult is not null) return preparation.ImmediateResult;
        var prepared = preparation.Execution!;
        var manifest = prepared.Manifest;
        var deploymentHash = prepared.DeploymentHash;
        var input = prepared.Input;
        var currentDirectory = prepared.CurrentDirectory;
        var logsPath = prepared.LogsPath;
        var resultPath = prepared.ResultPath;
        var statePath = prepared.StatePath;
        var startedAt = prepared.StartedAtUtc;
        var timeout = prepared.Timeout;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        var activeExecution = new ActiveExecution(linked);
        if (!active.TryAdd(request.ExecutionId, activeExecution))
        {
            if (active.TryGetValue(request.ExecutionId, out existingExecution))
                return await existingExecution.Completion.Task.WaitAsync(cancellationToken);
            return AppActionExecutionResultFactory.Create(
                request.ExecutionId,
                AppModuleExecutionStatuses.Lost,
                null,
                AppModuleExecutionErrorCodes.AmbiguousDispatch,
                "Runner execution ownership changed before acceptance could be confirmed.",
                deploymentHash,
                startedAt,
                logs: false,
                result: false);
        }

        WriteExecutionState(statePath, new RunnerActionExecutionState(false, new AppActionExecutionResult
        {
            ExecutionId = request.ExecutionId,
            Status = AppModuleExecutionStatuses.Running,
            DeploymentHash = deploymentHash,
            StartedAtUtc = startedAt
        }));

        AppActionExecutionResult Finish(AppActionExecutionResult result)
        {
            WriteExecutionState(statePath, new RunnerActionExecutionState(true, result));
            activeExecution.Completion.TrySetResult(result);
            return result;
        }

        try
        {
            var result = await AppActionProcessExecutor.ExecuteAsync(
                request, manifest, currentDirectory, input, logsPath, resultPath, deploymentHash, startedAt,
                cancellationToken, linked.Token, () => activeExecution.ManualCancellationRequested);
            return Finish(result);
        }
        catch (Win32Exception ex)
        {
            return Finish(AppActionExecutionResultFactory.Create(request.ExecutionId, AppModuleExecutionStatuses.Failed, null,
                AppModuleExecutionErrorCodes.RuntimeUnavailable, Limit(ex.Message, 1000), deploymentHash, startedAt, false, false));
        }
        finally
        {
            if (!activeExecution.Completion.Task.IsCompleted)
            {
                var interrupted = AppActionExecutionResultFactory.Create(
                    request.ExecutionId,
                    AppModuleExecutionStatuses.Lost,
                    null,
                    AppModuleExecutionErrorCodes.AmbiguousDispatch,
                    "Runner execution ended without a confirmed result.",
                    deploymentHash,
                    startedAt,
                    logs: false,
                    result: false);
                WriteExecutionState(statePath, new RunnerActionExecutionState(true, interrupted));
                activeExecution.Completion.TrySetResult(interrupted);
            }
            active.TryRemove(request.ExecutionId, out _);
        }
        }
        finally
        {
            concurrencyGate.Release();
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

    private sealed record RunnerActionExecutionState(bool Terminal, AppActionExecutionResult Result);
}
