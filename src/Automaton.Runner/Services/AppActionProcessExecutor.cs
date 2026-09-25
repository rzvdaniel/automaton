using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Automaton.Core.Models;

namespace Automaton.Runner.Console.Services;

internal static class AppActionProcessExecutor
{
    private const int MaximumResultBytes = 1_048_576;
    private const int MaximumLogBytes = 4_194_304;

    public static async Task<AppActionExecutionResult> ExecuteAsync(
        ExecuteAppActionModuleRequest request,
        AppActionManifest manifest,
        string currentDirectory,
        string input,
        string logsPath,
        string resultPath,
        string? deploymentHash,
        DateTime startedAt,
        CancellationToken callerToken,
        CancellationToken executionToken,
        Func<bool> manualCancellationRequested)
    {
        var secrets = GetSecretValues(request);
        using var process = StartActionProcess(manifest, currentDirectory, request.Secrets);
        var outputTasks = DrainProcessOutput(process);
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), executionToken);
        }
        catch (IOException) when (process.HasExited)
        {
            // A fast action can exit before consuming its request. Its exit status and output determine
            // the result; a broken input pipe should not discard that completed execution.
        }
        finally
        {
            process.StandardInput.Close();
        }

        try
        {
            await process.WaitForExitAsync(executionToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return await CompleteCancelledExecutionAsync(
                request, outputTasks, logsPath, secrets, deploymentHash, startedAt,
                callerToken.IsCancellationRequested || manualCancellationRequested());
        }

        return await CompleteExitedExecutionAsync(
            request, manifest, process, outputTasks, logsPath, resultPath, secrets, deploymentHash, startedAt);
    }

    private static Process StartActionProcess(
        AppActionManifest manifest,
        string currentDirectory,
        IReadOnlyDictionary<string, string> secrets)
    {
        var startInfo = CreateStartInfo(currentDirectory, manifest);
        foreach (var secret in secrets) startInfo.Environment[secret.Key] = secret.Value;
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Action runtime could not be started.");
        return process;
    }

    private static (Task<StreamCapture> Stdout, Task<StreamCapture> Stderr) DrainProcessOutput(Process process)
    {
        // Drain both pipes through EOF so a full pipe cannot deadlock the child process.
        var stdout = ReadStreamAsync(process.StandardOutput, MaximumResultBytes, truncate: false);
        var stderr = ReadStreamAsync(process.StandardError, MaximumLogBytes, truncate: true);
        return (stdout, stderr);
    }

    private static async Task<AppActionExecutionResult> CompleteCancelledExecutionAsync(
        ExecuteAppActionModuleRequest request,
        (Task<StreamCapture> Stdout, Task<StreamCapture> Stderr) outputTasks,
        string logsPath,
        IReadOnlyCollection<string> secrets,
        string? deploymentHash,
        DateTime startedAt,
        bool cancelled)
    {
        var status = cancelled ? AppModuleExecutionStatuses.Cancelled : AppModuleExecutionStatuses.TimedOut;
        var errorCode = cancelled ? AppModuleExecutionErrorCodes.Cancelled : AppModuleExecutionErrorCodes.TimedOut;
        var stderr = await outputTasks.Stderr;
        await outputTasks.Stdout;
        await File.WriteAllTextAsync(logsPath, RedactSecrets(stderr.Content, secrets), CancellationToken.None);
        return AppActionExecutionResultFactory.Create(
            request.ExecutionId, status, null, errorCode, errorCode, deploymentHash, startedAt, logs: true, result: false);
    }

    private static async Task<AppActionExecutionResult> CompleteExitedExecutionAsync(
        ExecuteAppActionModuleRequest request,
        AppActionManifest manifest,
        Process process,
        (Task<StreamCapture> Stdout, Task<StreamCapture> Stderr) outputTasks,
        string logsPath,
        string resultPath,
        IReadOnlyCollection<string> secrets,
        string? deploymentHash,
        DateTime startedAt)
    {
        var stdout = await outputTasks.Stdout;
        var stderr = await outputTasks.Stderr;
        var redactedStdout = RedactSecrets(stdout.Content, secrets);
        var redactedStderr = RedactSecrets(stderr.Content, secrets);
        await File.WriteAllTextAsync(logsPath, redactedStderr, CancellationToken.None);
        if (stdout.ExceededLimit)
            return CreateFailedResult(request, process.ExitCode, AppModuleExecutionErrorCodes.ResultTooLarge,
                "Action output exceeded its configured limit.", deploymentHash, startedAt);
        if (process.ExitCode != 0)
            return CreateFailedResult(request, process.ExitCode, AppModuleExecutionErrorCodes.ProcessFailed,
                Limit(redactedStderr, 1000), deploymentHash, startedAt);

        var validationError = ValidateOutput(redactedStdout, manifest.Spec.Output);
        if (validationError is not null)
            return CreateFailedResult(request, process.ExitCode, AppModuleExecutionErrorCodes.InvalidResult,
                validationError, deploymentHash, startedAt);
        await File.WriteAllTextAsync(resultPath, redactedStdout, CancellationToken.None);
        return AppActionExecutionResultFactory.Create(
            request.ExecutionId, AppModuleExecutionStatuses.Succeeded, process.ExitCode, null, null,
            deploymentHash, startedAt, logs: true, result: true);
    }

    private static AppActionExecutionResult CreateFailedResult(
        ExecuteAppActionModuleRequest request,
        int exitCode,
        string errorCode,
        string summary,
        string? deploymentHash,
        DateTime startedAt)
        => AppActionExecutionResultFactory.Create(
            request.ExecutionId, AppModuleExecutionStatuses.Failed, exitCode, errorCode, summary,
            deploymentHash, startedAt, logs: true, result: false);

    private static string? ValidateOutput(string output, Dictionary<string, object?>? schema)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            return AppActionSchemaValidator.Validate(document.RootElement, schema);
        }
        catch (JsonException)
        {
            return "Action stdout was not valid JSON.";
        }
    }

    private static IReadOnlyCollection<string> GetSecretValues(ExecuteAppActionModuleRequest request)
        => request.Secrets.Values
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length)
            .ToArray();

    private static ProcessStartInfo CreateStartInfo(string currentDirectory, AppActionManifest manifest)
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
                if (remainingBytes > 0) builder.Append(TruncateUtf8(chunk, remainingBytes));
                continue;
            }
            builder.Append(chunk);
        }
        var content = builder.ToString();
        if (truncate && exceededLimit) content += "\n[logs truncated by Automaton]";
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

    private static string RedactSecrets(string value, IReadOnlyCollection<string> secrets)
    {
        var redacted = value;
        foreach (var secret in secrets)
        {
            redacted = redacted.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            var jsonEncoded = JsonSerializer.Serialize(secret);
            if (jsonEncoded.Length > 2) redacted = redacted.Replace(jsonEncoded[1..^1], "[REDACTED]", StringComparison.Ordinal);
        }
        return redacted;
    }

    private static string Limit(string value, int max)
        => value.Length <= max ? value : value[..max];

    private sealed record StreamCapture(string Content, bool ExceededLimit);
}
