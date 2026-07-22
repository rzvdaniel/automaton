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
    private readonly AppActionExecutionCoordinator coordinator;
    private readonly ConcurrentDictionary<Guid, ActiveExecution> active = new();
    private readonly SemaphoreSlim concurrencyGate = new(4, 4);

    public AppActionExecutionService(RunnerWorkspaceService workspace, AppActionExecutionCoordinator? coordinator = null)
    {
        this.workspace = workspace;
        this.coordinator = coordinator ?? new AppActionExecutionCoordinator();
    }

    public async Task<AppActionExecutionResult> ExecuteAsync(ExecuteAppActionModuleRequest request, CancellationToken cancellationToken = default)
    {
        await concurrencyGate.WaitAsync(cancellationToken);
        try
        {
        using var actionLock = await coordinator.EnterAsync(request.AppId, request.ActionKey, cancellationToken);
        AppActionManifestParser.ValidateKey(request.ActionKey);
        var currentDirectory = workspace.GetAppActionCurrentDirectory(request.AppId, request.ActionKey);
        var manifestPath = Path.Combine(currentDirectory, "action.yaml");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"Action '{request.ActionKey}' is not deployed.");

        var allowPrivileged = request.SystemInfrastructure
            && Automaton.Core.Enums.RunnerPurposes.Normalize(Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"))
                == Automaton.Core.Enums.RunnerPurposes.Infrastructure;
        var manifest = AppActionManifestParser.ParseFile(manifestPath, request.ActionKey, allowPrivilegedCapabilities: allowPrivileged);
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
        var secretValues = request.Secrets.Values
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length)
            .ToArray();
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
            // Always drain both pipes until EOF. Stopping reads when a limit is reached can
            // fill the OS pipe and deadlock a child process before it exits.
            var stdoutTask = ReadStreamAsync(process.StandardOutput, MaximumResultBytes, truncate: false);
            var stderrTask = ReadStreamAsync(process.StandardError, MaximumLogBytes, truncate: true);
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
                var cancelledStderr = await stderrTask;
                await stdoutTask;
                await File.WriteAllTextAsync(logsPath, RedactSecrets(cancelledStderr.Content, secretValues), CancellationToken.None);
                return Completed(request.ExecutionId, status, null, errorCode, errorCode, deploymentHash, startedAt, logs: true, result: false);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var redactedStdout = RedactSecrets(stdout.Content, secretValues);
            var redactedStderr = RedactSecrets(stderr.Content, secretValues);
            await File.WriteAllTextAsync(logsPath, redactedStderr, CancellationToken.None);
            if (stdout.ExceededLimit)
                return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, process.ExitCode,
                    AppModuleExecutionErrorCodes.ResultTooLarge, "Action output exceeded its configured limit.",
                    deploymentHash, startedAt, logs: true, result: false);
            if (process.ExitCode != 0)
                return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, process.ExitCode, AppModuleExecutionErrorCodes.ProcessFailed, Limit(redactedStderr, 1000), deploymentHash, startedAt, true, false);
            try
            {
                using var outputDocument = JsonDocument.Parse(redactedStdout);
                var outputValidationError = AppActionSchemaValidator.Validate(outputDocument.RootElement, manifest.Spec.Output);
                if (outputValidationError != null)
                    return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, process.ExitCode,
                        AppModuleExecutionErrorCodes.InvalidResult, outputValidationError, deploymentHash, startedAt, true, false);
            }
            catch (JsonException)
            {
                return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, process.ExitCode, AppModuleExecutionErrorCodes.InvalidResult, "Action stdout was not valid JSON.", deploymentHash, startedAt, true, false);
            }
            await File.WriteAllTextAsync(resultPath, redactedStdout, CancellationToken.None);
            return Completed(request.ExecutionId, AppModuleExecutionStatuses.Succeeded, process.ExitCode, null, null, deploymentHash, startedAt, true, true);
        }
        catch (Win32Exception ex)
        {
            return Completed(request.ExecutionId, AppModuleExecutionStatuses.Failed, null,
                AppModuleExecutionErrorCodes.RuntimeUnavailable, Limit(ex.Message, 1000), deploymentHash, startedAt, false, false);
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

    private static async Task<StreamCapture> ReadStreamAsync(StreamReader reader, int maxBytes, bool truncate)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        var exceededLimit = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (read == 0) break;
            var chunk = new string(buffer, 0, read);
            if (Encoding.UTF8.GetByteCount(chunk) + Encoding.UTF8.GetByteCount(builder.ToString()) > maxBytes)
            {
                exceededLimit = true;
                if (truncate) continue;

                var remainingBytes = Math.Max(0, maxBytes - Encoding.UTF8.GetByteCount(builder.ToString()));
                if (remainingBytes > 0)
                    builder.Append(TruncateUtf8(chunk, remainingBytes));
                continue;
            }
            builder.Append(chunk);
        }
        var content = builder.ToString();
        if (truncate && exceededLimit)
            content += "\n[logs truncated by Automaton]";
        return new StreamCapture(content, exceededLimit);
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            var candidate = builder.ToString() + character;
            if (Encoding.UTF8.GetByteCount(candidate) > maxBytes) break;
            builder.Append(character);
        }
        return builder.ToString();
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

    private static string RedactSecrets(string value, IReadOnlyCollection<string> secretValues)
    {
        var redacted = value;
        foreach (var secret in secretValues)
        {
            redacted = redacted.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            var jsonEncoded = JsonSerializer.Serialize(secret);
            if (jsonEncoded.Length > 2)
                redacted = redacted.Replace(jsonEncoded[1..^1], "[REDACTED]", StringComparison.Ordinal);
        }
        return redacted;
    }

    private sealed class ActiveExecution(CancellationTokenSource source)
    {
        public CancellationTokenSource Source { get; } = source;
        public volatile bool ManualCancellationRequested;
    }

    private sealed record StreamCapture(string Content, bool ExceededLimit);
}
