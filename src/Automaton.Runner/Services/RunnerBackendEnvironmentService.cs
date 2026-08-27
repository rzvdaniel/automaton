using Serilog;
using static System.Console;

namespace Automaton.Runner.Console.Services;

public class RunnerBackendEnvironmentService
{
    private static readonly char[] NewLineChars = ['\r', '\n'];
    private readonly RunnerWorkspaceService runnerWorkspaceService;
    private readonly ILogger logger;

    public RunnerBackendEnvironmentService(RunnerWorkspaceService runnerWorkspaceService)
    {
        this.runnerWorkspaceService = runnerWorkspaceService;
        logger = Log.ForContext<RunnerBackendEnvironmentService>();
    }

    public List<string> EnsureAppVirtualEnvironment(
        Guid appId,
        Guid deploymentTargetId,
        bool recreate)
    {
        var logs = new List<string>();
        var venvDir = runnerWorkspaceService.GetLegacyControllerVenvDirectory(appId, deploymentTargetId);
        var appRootDir = runnerWorkspaceService.GetDeploymentTargetRoot(appId, deploymentTargetId);

        Directory.CreateDirectory(appRootDir);

        if (recreate && Directory.Exists(venvDir))
        {
            logs.Add($"Rebuilding app virtual environment: {venvDir}");
            Directory.Delete(venvDir, recursive: true);
        }

        var pythonPath = GetVenvPythonPath(venvDir);
        if (File.Exists(pythonPath))
        {
            logs.Add($"Using existing app virtual environment: {pythonPath}");
            return logs;
        }

        logs.Add($"Creating app virtual environment: {venvDir}");
        WriteLine($"Creating app virtual environment for app {appId}...");
        logger.Debug("Creating app virtual environment for app {AppId} at {VenvDir}", appId, venvDir);

        var (_, stdout, stderr) = RunProcessCapture(
            "python",
            $"-m venv --system-site-packages \"{venvDir}\"",
            timeoutMs: 120_000);

        AppendProcessLogs(logs, stdout, stderr);

        if (!File.Exists(pythonPath))
        {
            throw new InvalidOperationException($"Virtual environment created without python executable at {pythonPath}");
        }

        return logs;
    }

    public List<string> InstallRequirements(
        Guid appId,
        Guid deploymentTargetId,
        string requirementsPath)
    {
        var logs = new List<string>();
        logs.AddRange(EnsureAppVirtualEnvironment(appId, deploymentTargetId, recreate: true));

        var venvDir = runnerWorkspaceService.GetLegacyControllerVenvDirectory(appId, deploymentTargetId);
        var pipPath = GetVenvPipPath(venvDir);
        var pythonPath = GetVenvPythonPath(venvDir);

        logs.Add($"Installing packages from {Path.GetFileName(requirementsPath)}...");
        WriteLine($"Installing packages for app {appId}...");
        logger.Debug("Installing packages for app {AppId} from {ReqPath} into {VenvDir}", appId, requirementsPath, venvDir);

        var (_, stdout, stderr) = RunProcessCapture(
            pipPath,
            $"install -r \"{requirementsPath}\" --no-color --progress-bar off --disable-pip-version-check",
            timeoutMs: 300_000);

        AppendProcessLogs(logs, stdout, stderr);
        logs.Add($"App virtual environment ready: {pythonPath}");
        return logs;
    }

    public List<string> InstallSystemPackages(
        Guid appId,
        Guid deploymentTargetId,
        string packageListContent)
    {
        var logs = new List<string>();
        if (!IsRunningInContainer())
        {
            logs.Add("system-requirements.txt is only supported on Docker runners; skipped on this runner.");
            logger.Warning("system-requirements.txt skipped for app {AppId} because the runner is not in a container", appId);
            return logs;
        }

        var packages = packageListContent
            .Split(NewLineChars, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#') && !string.IsNullOrWhiteSpace(line))
            .Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^[a-zA-Z0-9][a-zA-Z0-9\-_.+]*$"))
            .ToList();

        if (packages.Count == 0)
        {
            logs.Add("No system packages to install.");
            return logs;
        }

        var packageList = string.Join(" ", packages);
        logs.Add($"Installing system packages: {packageList}");
        WriteLine($"Installing system packages for app {appId}: {packageList}");

        try
        {
            RunProcessCapture("sudo", "apt-get update -qq", timeoutMs: 60_000);
            var (_, stdout, stderr) = RunProcessCapture(
                "sudo",
                $"-E apt-get install -y --no-install-recommends {packageList}",
                timeoutMs: 300_000,
                environment: new Dictionary<string, string> { ["DEBIAN_FRONTEND"] = "noninteractive" });

            AppendProcessLogs(logs, stdout, stderr);
            logs.Add("System packages installed successfully.");
        }
        catch (Exception ex)
        {
            logs.Add($"System package install failed: {ex.Message}");
            logger.Error(ex, "Failed to install system packages for app {AppId}", appId);
        }

        return logs;
    }

    public static string GetVenvPythonPath(string venvDir)
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(venvDir, "Scripts", "python.exe")
            : Path.Combine(venvDir, "bin", "python");
    }

    private static string GetVenvPipPath(string venvDir)
    {
        return OperatingSystem.IsWindows()
            ? Path.Combine(venvDir, "Scripts", "pip.exe")
            : Path.Combine(venvDir, "bin", "pip");
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcessCapture(
        string fileName,
        string arguments,
        int timeoutMs,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (environment != null)
        {
            foreach (var pair in environment)
            {
                psi.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdoutTask = Task.Run(process.StandardOutput.ReadToEnd);
        var stderrTask = Task.Run(process.StandardError.ReadToEnd);

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException($"Process timed out after {timeoutMs / 1000}s: {fileName} {arguments}");
        }

        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process failed (exit code {process.ExitCode}): {fileName} {arguments}{Environment.NewLine}{stderr}".Trim());
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static void AppendProcessLogs(List<string> logs, string stdout, string stderr)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            logs.Add(line);
        }

        foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            logs.Add($"WARN: {line}");
        }
    }

    private static bool IsRunningInContainer()
    {
        return string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
            || File.Exists("/.dockerenv");
    }
}
