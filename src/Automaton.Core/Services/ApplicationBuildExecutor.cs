using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Automaton.Core.Models;

namespace Automaton.Core.Services;

public sealed class ApplicationBuildExecutionResult
{
    public bool Succeeded { get; init; }
    public int? ProcessId { get; init; }
    public int? ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Canceled { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> StandardOutput { get; init; } = [];
    public IReadOnlyList<string> StandardError { get; init; } = [];

    public IReadOnlyList<string> Logs =>
        StandardOutput
            .Select(line => $"stdout: {line}")
            .Concat(StandardError.Select(line => $"stderr: {line}"))
            .Concat(Error is null ? [] : [$"error: {Error}"])
            .ToArray();
}

/// <summary>
/// Executes the explicit application build command without inferring a language,
/// framework, package manager, or source layout.
/// </summary>
public static class ApplicationBuildExecutor
{
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(5);

    public static async Task<ApplicationBuildExecutionResult> ExecuteAsync(
        ApplicationBuildSpec build,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);

        if (build.Command is null)
            return new ApplicationBuildExecutionResult { Succeeded = true };

        var command = build.Command;
        // Full image requirements are validated on the complete application
        // contract before Runner deployment. This low-level executor remains
        // usable for local command tests and diagnostics.
        var fullWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(fullWorkspaceRoot))
            throw new DirectoryNotFoundException($"Build workspace does not exist: {fullWorkspaceRoot}");

        var workingDirectory = ResolveWorkspacePath(
            fullWorkspaceRoot,
            command.WorkingDirectory,
            "build working directory");
        if (!Directory.Exists(workingDirectory))
            throw new DirectoryNotFoundException($"Build working directory does not exist: {workingDirectory}");

        var startInfo = CreateStartInfo(command, workingDirectory);
        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Could not start build process: {command.Executable}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ApplicationBuildExecutionResult
            {
                Succeeded = false,
                Error = ex.Message,
                StandardError = [ex.Message]
            };
        }

        using (process)
        {
            return await ExecuteProcessAsync(process, build, cancellationToken);
        }
    }

    private static async Task<ApplicationBuildExecutionResult> ExecuteProcessAsync(
        Process process,
        ApplicationBuildSpec build,
        CancellationToken cancellationToken)
    {
        var command = build.Command!;
        var processId = process.Id;

        using var outputCancellation = new CancellationTokenSource();
        var stdoutTask = ReadOutputAsync(process.StandardOutput, outputCancellation.Token);
        var stderrTask = ReadOutputAsync(process.StandardError, outputCancellation.Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(build.TimeoutSeconds));

        var timedOut = false;
        var canceled = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            canceled = cancellationToken.IsCancellationRequested;
            timedOut = !canceled;
            await TerminateProcessTreeAsync(process);
        }

        var output = await CollectOutputAsync(stdoutTask, stderrTask, outputCancellation);
        var exitCode = TryGetExitCode(process);
        return new ApplicationBuildExecutionResult
        {
            Succeeded = !timedOut && !canceled && exitCode == 0 && !output.TimedOut,
            ProcessId = processId,
            ExitCode = exitCode,
            TimedOut = timedOut,
            Canceled = canceled,
            Error = output.TimedOut ? "Build output collection timed out." : null,
            StandardOutput = SplitLines(output.StandardOutput),
            StandardError = SplitLines(output.StandardError)
        };
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static async Task<OutputCollectionResult> CollectOutputAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask,
        CancellationTokenSource outputCancellation)
    {
        var outputTask = Task.WhenAll(stdoutTask, stderrTask);
        try
        {
            // The process cancellation token is used to stop pipe readers only
            // after the drain timeout; keep this bounded cleanup wait independent.
            var output = await outputTask.WaitAsync(OutputDrainTimeout, CancellationToken.None);
            return new OutputCollectionResult(output[0], output[1], TimedOut: false);
        }
        catch (TimeoutException)
        {
            outputCancellation.Cancel();
            try
            {
                var output = await outputTask.WaitAsync(
                    TimeSpan.FromMilliseconds(250), CancellationToken.None);
                return new OutputCollectionResult(output[0], output[1], TimedOut: true);
            }
            catch (TimeoutException)
            {
                return new OutputCollectionResult(
                    GetCompletedOutput(stdoutTask),
                    GetCompletedOutput(stderrTask),
                    TimedOut: true);
            }
        }
    }

    private static string GetCompletedOutput(Task<string> outputTask) =>
        outputTask.Status == TaskStatus.RanToCompletion ? outputTask.Result : string.Empty;

    private sealed record OutputCollectionResult(
        string StandardOutput,
        string StandardError,
        bool TimedOut);

    public static ProcessStartInfo CreateStartInfo(
        ApplicationCommandSpec command,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (command.UseShell)
        {
            var commandLine = string.Join(
                " ",
                new[] { command.Executable }
                    .Concat(command.Arguments)
                    .Select(QuoteShellArgument));
            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/s");
                startInfo.ArgumentList.Add("/c");
            }
            else
            {
                startInfo.FileName = "/bin/sh";
                startInfo.ArgumentList.Add("-c");
            }

            startInfo.ArgumentList.Add(commandLine);
        }
        else
        {
            startInfo.FileName = command.Executable;
            foreach (var argument in command.Arguments)
                startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in command.Environment)
            startInfo.Environment[pair.Key] = pair.Value;
        return startInfo;
    }

    public static string ResolveWorkspacePath(string root, string relativePath, string description)
        => ManagedPathBoundary.ResolveInside(root, relativePath, description);

    private static string[] SplitLines(string output)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (IsTerminationException(ex))
        {
        }

        if (OperatingSystem.IsWindows())
        {
            TryTaskKill(process.Id);
        }
        else
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception ex) when (IsTerminationException(ex))
            {
            }
        }

        using var waitCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(waitCancellation.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or COMException)
        {
        }
    }

    private static void TryTaskKill(int processId)
    {
        try
        {
            using var taskKill = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "/PID", processId.ToString(), "/T", "/F" }
            });
            taskKill?.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
        }
        catch (Exception ex) when (IsTerminationException(ex))
        {
        }
    }

    private static bool IsTerminationException(Exception exception) =>
        exception is InvalidOperationException
            or Win32Exception
            or NotSupportedException
            or PlatformNotSupportedException;

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or COMException)
        {
            return null;
        }
    }

    private static string QuoteShellArgument(string value)
        => OperatingSystem.IsWindows()
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
