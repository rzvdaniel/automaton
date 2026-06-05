using Automaton.Cli.Models;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Formats.Tar;
using System.ComponentModel;

namespace Automaton.Cli.Services;

public sealed class RunnerInstallService
{
    private const string DefaultDockerImage = "rzvdaniel/automaton-runner:latest";
    private const string RunnerDownloadsBase = "https://studio.automaton.monster/downloads/runner/latest";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ConfigService _configService;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    public RunnerInstallService(ConfigService configService)
    {
        _configService = configService;
    }

    private static string RunnerHome =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".automaton", "runner");

    private static string RunnerInstallsDir => Path.Combine(RunnerHome, "installs");
    private static string ActiveRunnerFile => Path.Combine(RunnerHome, "active-runner.txt");

    public RunnerCommandResult GetInstallStatus(string? runnerName = null)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new List<string>();

        try
        {
            var install = LoadInstall(runnerName);
            if (install == null)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, false, "no local runner install found"));
                nextSteps.Add("Run automaton runner install");
                return new RunnerCommandResult
                {
                    Success = false,
                    Summary = "Runner not installed",
                    Checks = checks,
                    NextSteps = nextSteps
                };
            }

            if (string.Equals(install.Mode, RunnerInstallModes.Docker, StringComparison.OrdinalIgnoreCase))
            {
                return GetDockerStatus(install, checks, nextSteps);
            }

            return GetNativeStatus(install, checks, nextSteps);
        }
        catch (Exception ex)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, false, ex.Message));
            return new RunnerCommandResult
            {
                Success = false,
                Summary = "Runner status failed",
                Checks = checks,
                NextSteps = nextSteps
            };
        }
    }

    public async Task<RunnerCommandResult> InstallAsync(RunnerInstallRequest request, CancellationToken cancellationToken = default)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new List<string>();
        request.ProgressReporter?.Invoke(string.Empty);

        var safeName = SanitizeRunnerName(request.RunnerName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, false, "runner name is required"));
            return new RunnerCommandResult { Success = false, Summary = "Runner install failed", Checks = checks };
        }

        Directory.CreateDirectory(RunnerInstallsDir);
        var existing = LoadInstall(safeName);
        if (existing != null)
        {
            if (!request.ReplaceExisting)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, false, $"runner '{safeName}' is already installed"));
                nextSteps.Add($"Rerun automaton runner install --name {safeName} --replace");
                return new RunnerCommandResult { Success = false, Summary = "Runner install cancelled", Checks = checks, NextSteps = nextSteps, Install = existing };
            }

            await UninstallAsync(existing.RunnerName, deleteData: false, force: true, cancellationToken: cancellationToken);
        }

        return string.Equals(request.Mode, RunnerInstallModes.Native, StringComparison.OrdinalIgnoreCase)
            ? await InstallNativeAsync(request, safeName, checks, nextSteps, cancellationToken)
            : await InstallDockerAsync(request, safeName, checks, nextSteps, cancellationToken);
    }

    public async Task<RunnerCommandResult> RestartAsync(string? runnerName, CancellationToken cancellationToken = default)
    {
        var install = LoadInstallOrThrow(runnerName);
        if (string.Equals(install.Mode, RunnerInstallModes.Docker, StringComparison.OrdinalIgnoreCase))
        {
            await RunProcessCheckedAsync("docker", $"restart {install.DockerContainerName}", cancellationToken);
            return GetInstallStatus(install.RunnerName);
        }

        if (OperatingSystem.IsWindows())
        {
            await RunProcessCheckedAsync("powershell", $"-NoProfile -Command \"Restart-Service -Name '{install.ServiceName}' -Force\"", cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await RunProcessCheckedAsync("systemctl", $"restart {install.ServiceName}", cancellationToken);
        }
        else if (OperatingSystem.IsMacOS())
        {
            await RunProcessCheckedAsync("launchctl", $"kickstart -k gui/{Environment.UserName}/{install.ServiceName}", cancellationToken);
        }

        return GetInstallStatus(install.RunnerName);
    }

    public async Task<RunnerCommandResult> StopAsync(string? runnerName, CancellationToken cancellationToken = default)
    {
        var install = LoadInstallOrThrow(runnerName);
        if (string.Equals(install.Mode, RunnerInstallModes.Docker, StringComparison.OrdinalIgnoreCase))
        {
            await RunProcessCheckedAsync("docker", $"stop {install.DockerContainerName}", cancellationToken);
            return GetInstallStatus(install.RunnerName);
        }

        if (OperatingSystem.IsWindows())
        {
            await RunProcessCheckedAsync("powershell", $"-NoProfile -Command \"Stop-Service -Name '{install.ServiceName}' -Force\"", cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await RunProcessCheckedAsync("systemctl", $"stop {install.ServiceName}", cancellationToken);
        }
        else if (OperatingSystem.IsMacOS())
        {
            await RunProcessCheckedAsync("launchctl", $"bootout gui/{Environment.UserName} {install.ServiceDefinitionPath}", cancellationToken);
        }

        return GetInstallStatus(install.RunnerName);
    }

    public async Task TailLogsAsync(string? runnerName, CancellationToken cancellationToken = default)
    {
        var install = LoadInstallOrThrow(runnerName);
        if (string.Equals(install.Mode, RunnerInstallModes.Docker, StringComparison.OrdinalIgnoreCase))
        {
            await RunProcessStreamingAsync("docker", $"logs -f {install.DockerContainerName}", cancellationToken);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            if (string.IsNullOrWhiteSpace(install.LogsPath))
                throw new InvalidOperationException("No log path recorded for this runner.");
            await RunProcessStreamingAsync("powershell", $"-NoProfile -Command \"Get-Content -LiteralPath '{install.LogsPath}' -Wait -Tail 50\"", cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await RunProcessStreamingAsync("journalctl", $"-u {install.ServiceName} -f", cancellationToken);
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (string.IsNullOrWhiteSpace(install.LogsPath))
                throw new InvalidOperationException("No log path recorded for this runner.");
            await RunProcessStreamingAsync("tail", $"-f \"{install.LogsPath}\"", cancellationToken);
        }
    }

    public async Task<RunnerCommandResult> UninstallAsync(string? runnerName, bool deleteData, bool force = false, CancellationToken cancellationToken = default)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var install = LoadInstall(runnerName);
        if (install == null)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, false, "runner not installed"));
            return new RunnerCommandResult { Success = false, Summary = "Runner uninstall failed", Checks = checks };
        }

        if (string.Equals(install.Mode, RunnerInstallModes.Docker, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await DockerContainerExistsAsync(install.DockerContainerName!, cancellationToken);
            if (exists)
            {
                await RunProcessCheckedAsync("docker", $"rm -f {install.DockerContainerName}", cancellationToken);
            }

            if (deleteData && !string.IsNullOrWhiteSpace(install.DockerVolumeName))
            {
                await RunProcessCheckedAsync("docker", $"volume rm {install.DockerVolumeName}", cancellationToken);
            }
        }
        else
        {
            await RemoveNativeServiceAsync(install, cancellationToken);
            if (deleteData && Directory.Exists(install.InstallRoot))
            {
                Directory.Delete(install.InstallRoot, recursive: true);
            }
        }

        var installFile = GetInstallRecordPath(install.SafeRunnerName);
        if (File.Exists(installFile))
        {
            File.Delete(installFile);
        }

        if (string.Equals(ReadActiveRunnerSafeName(), install.SafeRunnerName, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(ActiveRunnerFile);
        }

        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, true, $"runner '{install.RunnerName}' removed"));
        return new RunnerCommandResult
        {
            Success = true,
            Summary = "Runner uninstalled",
            Checks = checks
        };
    }

    public async Task EnsureDockerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RunProcessCheckedAsync("docker", "--version", cancellationToken);
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException(
                "Docker is not installed. Install Docker Desktop or Docker Engine, then rerun automaton runner install.");
        }
        catch (InvalidOperationException ex) when (IsMissingCommandError(ex.Message))
        {
            throw new InvalidOperationException(
                "Docker is not installed. Install Docker Desktop or Docker Engine, then rerun automaton runner install.");
        }

        try
        {
            await RunProcessCheckedAsync("docker", "info", cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsDockerDaemonUnavailable(ex.Message))
        {
            throw new InvalidOperationException(
                "Docker is installed but not running. Start Docker and rerun automaton runner install.");
        }
    }

    public string GetDefaultServer(string? resolvedServer)
        => string.IsNullOrWhiteSpace(resolvedServer) ? ConfigService.DefaultHostedServer : resolvedServer.TrimEnd('/');

    public string GetDefaultDockerImage()
        => Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_IMAGE")?.Trim()
           ?? DefaultDockerImage;

    public string GetDownloadUrl()
    {
        var overrideUrl = Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_DOWNLOAD_URL");
        if (!string.IsNullOrWhiteSpace(overrideUrl))
        {
            return overrideUrl.Trim();
        }

        var fileName = GetRunnerArchiveFileName();
        return $"{RunnerDownloadsBase}/{fileName}";
    }

    public static string SanitizeRunnerName(string runnerName)
    {
        var safe = new string(runnerName
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

        while (safe.Contains("--", StringComparison.Ordinal))
        {
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        }

        return safe.Trim('-');
    }

    private async Task<RunnerCommandResult> InstallNativeAsync(
        RunnerInstallRequest request,
        string safeName,
        List<CommandDiagnosticCheck> checks,
        List<string> nextSteps,
        CancellationToken cancellationToken)
    {
        request.ProgressReporter?.Invoke("Installing native runner...");
        var installRoot = Path.Combine(RunnerInstallsDir, safeName);
        var logsDir = Path.Combine(installRoot, "logs");
        var extractedDir = Path.Combine(installRoot, "runner");
        var dataDir = Path.Combine(installRoot, "data");
        var envFilePath = Path.Combine(installRoot, "runner.env");
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(logsDir);
        Directory.CreateDirectory(dataDir);

        checks.Add(await CheckPythonAsync(cancellationToken));

        var downloadUrl = GetDownloadUrl();
        request.ProgressReporter?.Invoke("Downloading native runner package...");
        var archivePath = Path.Combine(installRoot, Path.GetFileName(new Uri(downloadUrl).AbsolutePath));
        await DownloadFileAsync(downloadUrl, archivePath, cancellationToken);
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, true, $"downloaded runner archive from {downloadUrl}"));

        if (Directory.Exists(extractedDir))
        {
            Directory.Delete(extractedDir, recursive: true);
        }

        Directory.CreateDirectory(extractedDir);
        request.ProgressReporter?.Invoke("Extracting native runner package...");
        ExtractArchive(archivePath, extractedDir);

        var binaryPath = ResolveExtractedBinaryPath(extractedDir);
        var logsPath = Path.Combine(logsDir, "runner.log");
        var serviceName = $"automaton-runner-{safeName}";
        File.WriteAllText(envFilePath, BuildEnvFile(request.ApiServerUrl, request.ApiKey, request.RunnerName, dataDir));

        var install = new RunnerInstallRecord
        {
            RunnerName = request.RunnerName,
            SafeRunnerName = safeName,
            Mode = RunnerInstallModes.Native,
            ApiServerUrl = request.ApiServerUrl,
            InstallRoot = installRoot,
            BinaryPath = binaryPath,
            ServiceName = serviceName,
            LogsPath = logsPath,
            EnvFilePath = envFilePath,
            AutoStart = request.AutoStart,
            InstalledAtUtc = DateTime.UtcNow
        };

        request.ProgressReporter?.Invoke("Creating native runner service...");
        await InstallNativeServiceAsync(install, cancellationToken);
        SaveInstallRecord(install);
        SetActiveRunner(install.SafeRunnerName);

        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, true, "native runner service installed"));
        nextSteps.Add("Run automaton runner status");
        nextSteps.Add("Run automaton runner logs");
        nextSteps.Add("Open Automaton Studio > Runners and confirm the runner is connected");

        return new RunnerCommandResult
        {
            Success = true,
            Summary = "Runner installed successfully",
            Checks = checks,
            NextSteps = nextSteps,
            Install = install
        };
    }

    private async Task<RunnerCommandResult> InstallDockerAsync(
        RunnerInstallRequest request,
        string safeName,
        List<CommandDiagnosticCheck> checks,
        List<string> nextSteps,
        CancellationToken cancellationToken)
    {
        request.ProgressReporter?.Invoke("Checking Docker...");
        await EnsureDockerAvailableAsync(cancellationToken);
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Docker, true, "docker is installed and reachable"));

        var image = request.DockerImage ?? GetDefaultDockerImage();
        var containerName = $"automaton-runner-{safeName}";
        var volumeName = $"automaton-runner-data-{safeName}";

        request.ProgressReporter?.Invoke("Pulling runner image...");
        await RunProcessCheckedAsync("docker", $"pull {image}", cancellationToken);
        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Docker, true, $"pulled image {image}"));

        var exists = await DockerContainerExistsAsync(containerName, cancellationToken);
        if (exists)
        {
            request.ProgressReporter?.Invoke("Replacing existing runner container...");
            await RunProcessCheckedAsync("docker", $"rm -f {containerName}", cancellationToken);
        }

        request.ProgressReporter?.Invoke("Creating runner data volume...");
        await RunProcessCheckedAsync("docker", $"volume create {volumeName}", cancellationToken);

        var restartPolicy = request.AutoStart ? "unless-stopped" : "no";
        var args = string.Join(" ", new[]
        {
            "run -d",
            $"--name {containerName}",
            $"--restart {restartPolicy}",
            $"-e AUTOMATON_SERVER_URL=\"{request.ApiServerUrl}\"",
            $"-e AUTOMATON_API_KEY=\"{request.ApiKey}\"",
            $"-e AUTOMATON_RUNNER_NAME=\"{request.RunnerName}\"",
            $"-v {volumeName}:/app/data",
            image
        });

        request.ProgressReporter?.Invoke("Starting runner container...");
        await RunProcessCheckedAsync("docker", args, cancellationToken);

        var install = new RunnerInstallRecord
        {
            RunnerName = request.RunnerName,
            SafeRunnerName = safeName,
            Mode = RunnerInstallModes.Docker,
            ApiServerUrl = request.ApiServerUrl,
            InstallRoot = Path.Combine(RunnerInstallsDir, safeName),
            BinaryPath = string.Empty,
            DockerContainerName = containerName,
            DockerVolumeName = volumeName,
            DockerImage = image,
            AutoStart = request.AutoStart,
            InstalledAtUtc = DateTime.UtcNow
        };

        Directory.CreateDirectory(install.InstallRoot);
        SaveInstallRecord(install);
        SetActiveRunner(install.SafeRunnerName);

        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, true, $"container {containerName} started"));
        nextSteps.Add($"Run docker logs -f {containerName}");
        nextSteps.Add("Run automaton runner status");
        nextSteps.Add("Open Automaton Studio > Runners and confirm the runner is connected");

        return new RunnerCommandResult
        {
            Success = true,
            Summary = "Runner installed successfully",
            Checks = checks,
            NextSteps = nextSteps,
            Install = install
        };
    }

    private async Task InstallNativeServiceAsync(RunnerInstallRecord install, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var serviceName = install.ServiceName!;
            var scriptPath = Path.Combine(install.InstallRoot, "service-wrapper.ps1");
            install.ServiceDefinitionPath = scriptPath;
            File.WriteAllText(scriptPath, BuildWindowsWrapperScript(install));
            var command = $"-NoProfile -Command \"New-Service -Name '{serviceName}' -BinaryPathName 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File \\\"{scriptPath}\\\"' -DisplayName 'Automaton Runner ({install.RunnerName})' -StartupType {(install.AutoStart ? "Automatic" : "Manual")}; Start-Service -Name '{serviceName}'\"";
            await RunProcessCheckedAsync("powershell", command, cancellationToken);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var servicePath = $"/etc/systemd/system/{install.ServiceName}.service";
            install.ServiceDefinitionPath = servicePath;
            var contents = BuildSystemdService(install);
            await WriteElevatedFileAsync(servicePath, contents, cancellationToken);
            await RunProcessCheckedAsync("systemctl", "daemon-reload", cancellationToken);
            if (install.AutoStart)
            {
                await RunProcessCheckedAsync("systemctl", $"enable {install.ServiceName}", cancellationToken);
            }
            await RunProcessCheckedAsync("systemctl", $"start {install.ServiceName}", cancellationToken);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var agentsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
            Directory.CreateDirectory(agentsDir);
            var plistPath = Path.Combine(agentsDir, $"{install.ServiceName}.plist");
            install.ServiceDefinitionPath = plistPath;
            File.WriteAllText(plistPath, BuildLaunchdPlist(install));
            await RunProcessCheckedAsync("launchctl", $"load -w \"{plistPath}\"", cancellationToken);
            return;
        }

        throw new PlatformNotSupportedException("Native runner install is not supported on this platform.");
    }

    private async Task RemoveNativeServiceAsync(RunnerInstallRecord install, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await RunProcessBestEffortAsync("powershell", $"-NoProfile -Command \"if (Get-Service -Name '{install.ServiceName}' -ErrorAction SilentlyContinue) {{ Stop-Service -Name '{install.ServiceName}' -Force -ErrorAction SilentlyContinue; sc.exe delete '{install.ServiceName}' | Out-Null }}\"", cancellationToken);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await RunProcessBestEffortAsync("systemctl", $"stop {install.ServiceName}", cancellationToken);
            await RunProcessBestEffortAsync("systemctl", $"disable {install.ServiceName}", cancellationToken);
            if (!string.IsNullOrWhiteSpace(install.ServiceDefinitionPath))
            {
                await RunProcessCheckedAsync("sudo", $"rm -f \"{install.ServiceDefinitionPath}\"", cancellationToken);
                await RunProcessBestEffortAsync("systemctl", "daemon-reload", cancellationToken);
            }
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            if (!string.IsNullOrWhiteSpace(install.ServiceDefinitionPath))
            {
                await RunProcessBestEffortAsync("launchctl", $"unload -w \"{install.ServiceDefinitionPath}\"", cancellationToken);
                if (File.Exists(install.ServiceDefinitionPath))
                {
                    File.Delete(install.ServiceDefinitionPath);
                }
            }
        }
    }

    private RunnerCommandResult GetNativeStatus(RunnerInstallRecord install, List<CommandDiagnosticCheck> checks, List<string> nextSteps)
    {
        var status = "unknown";
        if (OperatingSystem.IsWindows())
        {
            var output = RunProcessCapture("powershell", $"-NoProfile -Command \"(Get-Service -Name '{install.ServiceName}' -ErrorAction SilentlyContinue).Status\"");
            status = string.IsNullOrWhiteSpace(output.Stdout) ? "not installed" : output.Stdout.Trim();
        }
        else if (OperatingSystem.IsLinux())
        {
            var output = RunProcessCapture("systemctl", $"is-active {install.ServiceName}");
            status = output.ExitCode == 0 ? output.Stdout.Trim() : "inactive";
        }
        else if (OperatingSystem.IsMacOS())
        {
            var output = RunProcessCapture("launchctl", $"list {install.ServiceName}");
            status = output.ExitCode == 0 ? "loaded" : "not loaded";
        }

        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, true, $"native service status: {status}"));
        return new RunnerCommandResult
        {
            Success = true,
            Summary = "Runner status loaded",
            Checks = checks,
            NextSteps = nextSteps,
            Install = install,
            Status = status
        };
    }

    private RunnerCommandResult GetDockerStatus(RunnerInstallRecord install, List<CommandDiagnosticCheck> checks, List<string> nextSteps)
    {
        var output = RunProcessCapture("docker", $"inspect {install.DockerContainerName} --format \"{{{{.State.Status}}}}|{{{{.Config.Image}}}}|{{{{.HostConfig.RestartPolicy.Name}}}}\"");
        var status = "missing";
        string? image = null;
        string? restartPolicy = null;
        if (output.ExitCode == 0)
        {
            var parts = output.Stdout.Trim().Split('|');
            status = parts.ElementAtOrDefault(0) ?? "unknown";
            image = parts.ElementAtOrDefault(1);
            restartPolicy = parts.ElementAtOrDefault(2);
        }

        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Docker, output.ExitCode == 0, $"docker container status: {status}", output.ExitCode != 0));
        return new RunnerCommandResult
        {
            Success = output.ExitCode == 0,
            Summary = output.ExitCode == 0 ? "Runner status loaded" : "Runner container not found",
            Checks = checks,
            NextSteps = nextSteps,
            Install = install,
            Status = status,
            RestartPolicy = restartPolicy,
            ContainerImage = image
        };
    }

    private async Task<CommandDiagnosticCheck> CheckPythonAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunProcessCheckedAsync("python", "--version", cancellationToken);
            return new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerStart, true, "python detected for backend app execution");
        }
        catch
        {
            return new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.RunnerStart,
                false,
                "python not detected. The runner can still install, but Python 3.11+ is required for backend Python apps.",
                true);
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var file = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(file, cancellationToken);
    }

    private static void ExtractArchive(string archivePath, string destinationDirectory)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);
            return;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var archiveStream = File.OpenRead(archivePath);
            using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzipStream, destinationDirectory, overwriteFiles: true);
            return;
        }

        throw new InvalidOperationException($"Unsupported runner archive format: {archivePath}");
    }

    private static string ResolveExtractedBinaryPath(string extractedDirectory)
    {
        var fileName = OperatingSystem.IsWindows() ? "automaton-runner.exe" : "automaton-runner";
        var matches = Directory.GetFiles(extractedDirectory, fileName, SearchOption.AllDirectories);
        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"Could not find {fileName} in extracted runner archive.");
        }

        return matches[0];
    }

    private static string BuildEnvFile(string serverUrl, string apiKey, string runnerName, string dataDir)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"AUTOMATON_SERVER_URL={serverUrl}",
            $"AUTOMATON_API_KEY={apiKey}",
            $"AUTOMATON_RUNNER_NAME={runnerName}",
            $"RUNNER_SCRIPTS_DIR={Path.Combine(dataDir, "apps")}"
        });
    }

    private static string BuildWindowsWrapperScript(RunnerInstallRecord install)
    {
        return $@"$envFile = ""{install.EnvFilePath}""
Get-Content -LiteralPath $envFile | ForEach-Object {{
  if ($_ -match '^(.*?)=(.*)$') {{
    [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2])
  }}
}}
& ""{install.BinaryPath}"" service *>> ""{install.LogsPath}""";
    }

    private static string BuildSystemdService(RunnerInstallRecord install)
    {
        var user = Environment.UserName;
        return $"""
[Unit]
Description=Automaton Runner ({install.RunnerName})
After=network.target

[Service]
Type=simple
User={user}
WorkingDirectory={install.InstallRoot}
EnvironmentFile={install.EnvFilePath}
ExecStart={install.BinaryPath} service
Restart=always
RestartSec=10
StandardOutput=append:{install.LogsPath}
StandardError=append:{install.LogsPath}

[Install]
WantedBy=multi-user.target
""";
    }

    private static string BuildLaunchdPlist(RunnerInstallRecord install)
    {
        var envVars = ParseEnvFile(install.EnvFilePath!);
        var envXml = string.Join(Environment.NewLine, envVars.Select(kvp => $"    <key>{System.Security.SecurityElement.Escape(kvp.Key)}</key>{Environment.NewLine}    <string>{System.Security.SecurityElement.Escape(kvp.Value)}</string>"));
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>{install.ServiceName}</string>
  <key>ProgramArguments</key>
  <array>
    <string>{install.BinaryPath}</string>
    <string>service</string>
  </array>
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <{(install.AutoStart ? "true" : "false")}/>
  <key>WorkingDirectory</key>
  <string>{install.InstallRoot}</string>
  <key>StandardOutPath</key>
  <string>{install.LogsPath}</string>
  <key>StandardErrorPath</key>
  <string>{install.LogsPath}</string>
  <key>EnvironmentVariables</key>
  <dict>
{envXml}
  </dict>
</dict>
</plist>
""";
    }

    private static Dictionary<string, string> ParseEnvFile(string path)
    {
        return File.ReadAllLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains('='))
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    }

    private static string GetRunnerArchiveFileName()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        if (OperatingSystem.IsWindows())
        {
            return $"automaton-runner-win-{arch}.zip";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"automaton-runner-linux-{arch}.tar.gz";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"automaton-runner-macos-{arch}.tar.gz";
        }

        throw new PlatformNotSupportedException("Unsupported platform for runner downloads.");
    }

    private RunnerInstallRecord? LoadInstall(string? runnerName)
    {
        var safeName = string.IsNullOrWhiteSpace(runnerName)
            ? ReadActiveRunnerSafeName()
            : SanitizeRunnerName(runnerName);

        if (string.IsNullOrWhiteSpace(safeName))
        {
            return null;
        }

        var path = GetInstallRecordPath(safeName);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RunnerInstallRecord>(json, JsonOptions);
    }

    private RunnerInstallRecord LoadInstallOrThrow(string? runnerName)
        => LoadInstall(runnerName) ?? throw new InvalidOperationException("No local runner install found. Run automaton runner install first.");

    private void SaveInstallRecord(RunnerInstallRecord record)
    {
        Directory.CreateDirectory(RunnerInstallsDir);
        File.WriteAllText(GetInstallRecordPath(record.SafeRunnerName), JsonSerializer.Serialize(record, JsonOptions));
    }

    private static string GetInstallRecordPath(string safeRunnerName)
        => Path.Combine(RunnerInstallsDir, $"{safeRunnerName}.json");

    private static void SetActiveRunner(string safeRunnerName)
    {
        Directory.CreateDirectory(RunnerHome);
        File.WriteAllText(ActiveRunnerFile, safeRunnerName);
    }

    private static string? ReadActiveRunnerSafeName()
        => File.Exists(ActiveRunnerFile) ? File.ReadAllText(ActiveRunnerFile).Trim() : null;

    private static async Task<bool> DockerContainerExistsAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("docker", $"inspect {containerName}", cancellationToken, allowFailure: true);
        return result.ExitCode == 0;
    }

    private static async Task WriteElevatedFileAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, contents, cancellationToken);
        try
        {
            await RunProcessCheckedAsync("sudo", $"cp \"{tempPath}\" \"{path}\"", cancellationToken);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcessCapture(string fileName, string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static async Task RunProcessCheckedAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(fileName, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;
            throw new InvalidOperationException($"{fileName} {arguments} failed: {detail}".Trim());
        }
    }

    private static async Task RunProcessBestEffortAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        await RunProcessAsync(fileName, arguments, cancellationToken, allowFailure: true);
    }

    private static async Task RunProcessStreamingAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");
        await process.WaitForExitAsync(cancellationToken);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken,
        bool allowFailure = false)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (!allowFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments} failed: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr)}".Trim());
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static bool IsMissingCommandError(string message)
        => message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
           || message.Contains("not recognized as an internal or external command", StringComparison.OrdinalIgnoreCase)
           || message.Contains("The system cannot find the file specified", StringComparison.OrdinalIgnoreCase);

    private static bool IsDockerDaemonUnavailable(string message)
        => message.Contains("daemon", StringComparison.OrdinalIgnoreCase)
           || message.Contains("docker desktop is not running", StringComparison.OrdinalIgnoreCase)
           || message.Contains("cannot connect to the docker daemon", StringComparison.OrdinalIgnoreCase)
           || message.Contains("error during connect", StringComparison.OrdinalIgnoreCase)
           || message.Contains("docker engine", StringComparison.OrdinalIgnoreCase)
           || message.Contains("is the docker daemon running", StringComparison.OrdinalIgnoreCase);
}
