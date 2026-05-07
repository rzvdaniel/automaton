using Automaton.Cli.Models;
using System.Diagnostics;

namespace Automaton.Cli.Services;

public class PythonRuntimeService(ConfigService configService)
{
    private static readonly string[] PythonCommandCandidates = OperatingSystem.IsWindows()
        ? ["python.exe", "py.exe", "python", "py"]
        : ["python3", "python"];

    public async Task<PythonRuntimeInfo> ResolveAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var preferredCandidates = GetPreferredCandidates(projectRoot);

        foreach (var candidate in preferredCandidates.Concat(PythonCommandCandidates).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var runtime = await TryResolveCandidateAsync(candidate, cancellationToken);
            if (runtime is not null)
            {
                return runtime;
            }
        }

        throw new InvalidOperationException("Python runtime not found. Configure one with 'automaton config --python <path>' or install python3/python.");
    }

    public async Task<bool> IsDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = CreateProcess("docker", "--version");
            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<string> GetPreferredCandidates(string projectRoot)
    {
        var local = configService.LoadProjectLocalConfig(projectRoot);
        var global = configService.LoadGlobalConfig();

        var venvCandidates = new[]
        {
            Path.Combine(projectRoot, ".venv", OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python"),
            Path.Combine(projectRoot, "venv", OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python"),
            Path.Combine(projectRoot, "backend", ".venv", OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python"),
            Path.Combine(projectRoot, "backend", "venv", OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python")
        };

        foreach (var candidate in venvCandidates)
        {
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(local.PythonPath))
        {
            yield return local.PythonPath;
        }

        if (!string.IsNullOrWhiteSpace(global.PythonPath))
        {
            yield return global.PythonPath;
        }
    }

    private static async Task<PythonRuntimeInfo?> TryResolveCandidateAsync(string candidate, CancellationToken cancellationToken)
    {
        try
        {
            using var process = CreateProcess(candidate, "--version");
            process.Start();
            var stdOut = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                return null;
            }

            var version = string.IsNullOrWhiteSpace(stdOut) ? stdErr.Trim() : stdOut.Trim();
            return new PythonRuntimeInfo
            {
                ExecutablePath = candidate,
                Version = string.IsNullOrWhiteSpace(version) ? null : version,
                Source = ResolveSource(candidate)
            };
        }
        catch
        {
            return null;
        }
    }

    private static Process CreateProcess(string fileName, string arguments)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
    }

    private static string ResolveSource(string candidate)
    {
        if (candidate.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || candidate.Contains($"{Path.DirectorySeparatorChar}venv{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return "app-local-venv";
        }

        if (Path.IsPathRooted(candidate))
        {
            return "configured-path";
        }

        return "system";
    }
}
