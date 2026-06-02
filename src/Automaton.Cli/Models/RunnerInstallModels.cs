namespace Automaton.Cli.Models;

public static class RunnerInstallModes
{
    public const string Native = "native";
    public const string Docker = "docker";
}

public sealed class RunnerInstallRecord
{
    public string RunnerName { get; set; } = string.Empty;
    public string SafeRunnerName { get; set; } = string.Empty;
    public string Mode { get; set; } = RunnerInstallModes.Docker;
    public string ApiServerUrl { get; set; } = string.Empty;
    public string InstallRoot { get; set; } = string.Empty;
    public string BinaryPath { get; set; } = string.Empty;
    public string? ServiceName { get; set; }
    public string? ServiceDefinitionPath { get; set; }
    public string? LogsPath { get; set; }
    public string? EnvFilePath { get; set; }
    public string? DockerContainerName { get; set; }
    public string? DockerVolumeName { get; set; }
    public string? DockerImage { get; set; }
    public bool AutoStart { get; set; } = true;
    public DateTime InstalledAtUtc { get; set; }
}

public sealed class RunnerInstallRequest
{
    public string RunnerName { get; set; } = string.Empty;
    public string ApiServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Mode { get; set; } = RunnerInstallModes.Docker;
    public bool AutoStart { get; set; } = true;
    public string? DockerImage { get; set; }
    public bool ReplaceExisting { get; set; }
    public Action<string>? ProgressReporter { get; set; }
}

public sealed class RunnerCommandResult
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<CommandDiagnosticCheck> Checks { get; set; } = [];
    public List<string> NextSteps { get; set; } = [];
    public RunnerInstallRecord? Install { get; set; }
    public string? Status { get; set; }
    public string? RestartPolicy { get; set; }
    public string? ContainerImage { get; set; }
}
