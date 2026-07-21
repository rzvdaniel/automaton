using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

public sealed class AppActionExecutionService
{
    private const int MaximumInputBytes = 1_048_576;
    private const int MaximumResultBytes = 1_048_576;
    private const int MaximumLogBytes = 4_194_304;
    private static readonly TimeSpan ArtifactRetention = TimeSpan.FromHours(24);
    private readonly RunnerWorkspaceService workspace;
    private readonly ConcurrentDictionary<Guid, ActiveExecution> active = new();
    private readonly SemaphoreSlim concurrencyGate = new(4, 4);

    public AppActionExecutionService(RunnerWorkspaceService workspace)
    {
        this.workspace = workspace;
    }

    public async Task<AppActionExecutionResult> ExecuteAsync(ExecuteAppActionModuleRequest request, CancellationToken cancellationToken = default)
    {
        await concurrencyGate.WaitAsync(cancellationToken);
        try
        {
        AppActionManifestParser.ValidateKey(request.ActionKey);
        var currentDirectory = workspace.GetAppActionCurrentDirectory(request.AppId, request.ActionKey);
        var manifestPath = Path.Combine(currentDirectory, "action.yaml");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Action '{request.ActionKey}' is not deployed.");

        var manifest = AppActionManifestParser.ParseFile(manifestPath, request.ActionKey);
        var metadataPath = Path.Combine(currentDirectory, ".automaton-action-deployment.json");
        var deploymentHash = ReadDeploymentHash(metadataPath);
        if (!string.IsNullOrWhiteSpace(request.ExpectedDeploymentHash)
            && !string.Equals(request.ExpectedDeploymentHash, deploymentHash, StringComparison.Ordinal))
            throw new InvalidOperationException("The deployed Action hash does not match the requested hash.");

        var input = request.Input.ValueKind == JsonValueKind.Undefined ? "null" : request.Input.GetRawText();

        var executionDirectory = Path.Combine(workspace.GetAppActionExecutionsDirectory(request.AppId, request.ActionKey), request.ExecutionId.ToString("N"));
        Directory.CreateDirectory(executionDirectory);
        var logsPath = Path.Combine(executionDirectory, "stderr.log");
        var resultPath = Path.Combine(executionDirectory, "result.json");
        var startedAt = DateTime.UtcNow;
        if (Encoding.UTF8.GetByteCount(input) > MaximumInputBytes)
            return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, null, AppModuleExecutionErrorCodes.InvalidInput,
                "Action input exceeds the 1 MB limit.", deploymentHash, startedAt, logs: false, result: false);
        JsonElement inputElement;
        if (request.Input.ValueKind == JsonValueKind.Undefined)
        {
            using var nullDocument = JsonDocument.Parse("null");
            inputElement = nullDocument.RootElement.Clone();
        }
        else
        {
            inputElement = request.Input;
        }
        var inputValidationError = AppActionSchemaValidator.Validate(inputElement, manifest.Spec.Input);
        if (inputValidationError != null)
            return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, null, AppModuleExecutionErrorCodes.InvalidInput,
                inputValidationError, deploymentHash, startedAt, logs: false, result: false);
        var timeout = TimeSpan.FromSeconds(Math.Min(request.TimeoutSeconds ?? manifest.Spec.TimeoutSeconds, manifest.Spec.TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        var activeExecution = new ActiveExecution(linked);
        if (!active.TryAdd(request.ExecutionId, activeExecution))
            throw new InvalidOperationException("Execution is already active.");

        try
        {
            var startInfo = BuildStartInfo(currentDirectory, manifest);
            foreach (var secret in request.Secrets)
                startInfo.Environment[secret.Key] = secret.Value;
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Action runtime could not be started.");
            await process.StandardInput.WriteAsync(input.AsMemory(), linked.Token);
            process.StandardInput.Close();
            var stdoutTask = ReadLimitedAsync(process.StandardOutput, MaximumResultBytes, linked.Token);
            var stderrTask = ReadLimitedAsync(process.StandardError, MaximumLogBytes, linked.Token, truncate: true);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                var cancelled = cancellationToken.IsCancellationRequested || activeExecution.ManualCancellationRequested;
                var status = cancelled ? AppModuleExecutionStatuses.Cancelled : AppModuleExecutionStatuses.TimedOut;
                var errorCode = cancelled ? AppModuleExecutionErrorCodes.Cancelled : AppModuleExecutionErrorCodes.TimedOut;
                await File.WriteAllTextAsync(logsPath, await SafeReadAsync(stderrTask), CancellationToken.None);
                return Completed(request.ExecutionId, status, null, errorCode, errorCode, deploymentHash, startedAt, logs: true, result: false);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            await File.WriteAllTextAsync(logsPath, stderr, CancellationToken.None);
            if (process.ExitCode != 0)
                return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, process.ExitCode, AppModuleExecutionErrorCodes.ProcessFailed, Limit(stderr, 1000), deploymentHash, startedAt, true, false);
            try
            {
                using var outputDocument = JsonDocument.Parse(stdout);
                var outputValidationError = AppActionSchemaValidator.Validate(outputDocument.RootElement, manifest.Spec.Output);
                if (outputValidationError != null)
                    return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, process.ExitCode,
                        AppModuleExecutionErrorCodes.InvalidResult, outputValidationError, deploymentHash, startedAt, true, false);
            }
            catch (JsonException)
            {
                return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, process.ExitCode, AppModuleExecutionErrorCodes.InvalidResult, "Action stdout was not valid JSON.", deploymentHash, startedAt, true, false);
            }
            await File.WriteAllTextAsync(resultPath, stdout, CancellationToken.None);
            return Completed(request.ExecutionId, AppModuleExecutionStatuses.Succeeded, process.ExitCode, null, null, deploymentHash, startedAt, true, true);
        }
        catch (Win32Exception ex)
        {
            return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, null,
                AppModuleExecutionErrorCodes.RuntimeUnavailable, Limit(ex.Message, 1000), deploymentHash, startedAt, false, false);
        }
        catch (AppActionOutputLimitException)
        {
            return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, null,
                AppModuleExecutionErrorCodes.ResultTooLarge, "Action output exceeded its configured limit.",
                deploymentHash, startedAt, logs: false, result: false);
        }
        finally
        {
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
            if (!normalized.Contains("/runtime/actions/", StringComparison.OrdinalIgnoreCase)) continue;
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

    private AppModuleExecutionArtifactResponse ReadArtifact(ReadAppModuleExecutionArtifactRequest request, string fileName)
    {
        AppActionManifestParser.ValidateKey(request.ActionKey);
        var path = Path.Combine(workspace.GetAppActionExecutionsDirectory(request.AppId, request.ActionKey), request.ExecutionId.ToString("N"), fileName);
        if (!File.Exists(path)) return new(false, null, null, "Artifact is not available.");
        var expires = File.GetLastWriteTimeUtc(path) + ArtifactRetention;
        if (expires <= DateTime.UtcNow) return new(false, null, expires, "Artifact has expired.");
        return new(true, File.ReadAllText(path), expires);
    }

    private static ProcessStartInfo BuildStartInfo(string currentDirectory, AppActionManifest manifest)
    {
        var entrypoint = Path.GetFullPath(Path.Combine(currentDirectory, manifest.Spec.Entrypoint));
        var executable = manifest.Spec.Runtime == AppActionRuntimes.Python
            ? OperatingSystem.IsWindows()
                ? Path.Combine(currentDirectory, ".venv", "Scripts", "python.exe")
                : Path.Combine(currentDirectory, ".venv", "bin", "python")
            : "/bin/bash";
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = currentDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(entrypoint);
        return startInfo;
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maxBytes, CancellationToken cancellationToken, bool truncate = false)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            builder.Append(buffer, 0, read);
            if (Encoding.UTF8.GetByteCount(builder.ToString()) > maxBytes && truncate)
                return Limit(builder.ToString(), maxBytes / 2) + "\n[logs truncated by Automaton]";
            if (Encoding.UTF8.GetByteCount(builder.ToString()) > maxBytes)
                throw new AppActionOutputLimitException();
        }
        return builder.ToString();
    }

    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try { return await task; } catch { return string.Empty; }
    }

    private static AppActionExecutionResult Completed(Guid id, string status, int? exitCode, string? errorCode, string? summary, string? hash, DateTime started, bool logs, bool result)
    {
        var completed = DateTime.UtcNow;
        return new AppActionExecutionResult
        {
            ExecutionId = id, Status = status, ExitCode = exitCode, ErrorCode = errorCode,
            FailureSummary = summary, DeploymentHash = hash, StartedAtUtc = started, CompletedAtUtc = completed,
            LogsAvailable = logs, ResultAvailable = result,
            LogsExpireAtUtc = logs ? completed + ArtifactRetention : null,
            ResultExpireAtUtc = result ? completed + ArtifactRetention : null
        };
    }

    private static string? ReadDeploymentHash(string path)
    {
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("contentHash", out var value) ? value.GetString() : null;
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];

    private sealed class ActiveExecution(CancellationTokenSource source)
    {
        public CancellationTokenSource Source { get; } = source;
        public volatile bool ManualCancellationRequested;
    }

    private sealed class AppActionOutputLimitException : Exception;
}
