using System.Diagnostics;

namespace Automaton.Runner.Console.Services;

internal static class AppActionRuntimePreparer
{
    private static readonly TimeSpan DependencyPreparationTimeout = TimeSpan.FromMinutes(10);

    public static async Task PreparePythonAsync(string stagingDirectory, CancellationToken cancellationToken)
    {
        var python = OperatingSystem.IsWindows() ? "python" : "python3";
        await RunProcessAsync(python, ["-m", "venv", ".venv"], stagingDirectory, cancellationToken);
        var requirementsPath = Path.Combine(stagingDirectory, "requirements.txt");
        if (File.Exists(requirementsPath))
        {
            var venvPython = OperatingSystem.IsWindows()
                ? Path.Combine(stagingDirectory, ".venv", "Scripts", "python.exe")
                : Path.Combine(stagingDirectory, ".venv", "bin", "python");
            await RunProcessAsync(venvPython, ["-m", "pip", "install", "-r", "requirements.txt"], stagingDirectory, cancellationToken);
        }
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(DependencyPreparationTimeout);
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Runtime '{fileName}' could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Runtime preparation exceeded {DependencyPreparationTimeout.TotalMinutes:0} minutes.");
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Runtime preparation failed with exit code {process.ExitCode}: {Limit(error.Length > 0 ? error : output, 2000)}");
    }

    private static string Limit(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
