using System.Diagnostics;
using Automaton.Core.Models;

namespace Automaton.Core.Services;

public sealed class ApplicationBuildExecutionResult
{
    public bool Succeeded { get; init; }
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
    public static async Task<ApplicationBuildExecutionResult> ExecuteAsync(
        ApplicationBuildSpec build,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);

        if (build.Command is null)
            return new ApplicationBuildExecutionResult { Succeeded = true };

        var command = build.Command;
        ApplicationContractValidator.EnsureValid(new ApplicationContract { Build = build });
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
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
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var standardOutput = SplitLines(await stdoutTask);
        var standardError = SplitLines(await stderrTask);
        return new ApplicationBuildExecutionResult
        {
            Succeeded = !timedOut && !canceled && process.ExitCode == 0,
            ExitCode = process.HasExited ? process.ExitCode : null,
            TimedOut = timedOut,
            Canceled = canceled,
            StandardOutput = standardOutput,
            StandardError = standardError
        };
    }

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
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || (relativePath.Length >= 2 && char.IsLetter(relativePath[0]) && relativePath[1] == ':'))
            throw new InvalidOperationException($"{description} must remain inside the build workspace.");

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is ".."))
            throw new InvalidOperationException($"{description} must remain inside the build workspace.");

        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{description} must remain inside the build workspace.");
        return fullPath;
    }

    private static string[] SplitLines(string output)
        => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string QuoteShellArgument(string value)
        => OperatingSystem.IsWindows()
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
}
