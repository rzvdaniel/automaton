namespace Automaton.Runner.Console.Models;

public sealed class RunnerEnrollmentExchangeRequest
{
    public string EnrollmentToken { get; set; } = string.Empty;
    public string? RunnerName { get; set; }
    public string? RunnerVersion { get; set; }
    public string? MachineName { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Architecture { get; set; }
    public string? ImageVersion { get; set; }
}

public sealed class RunnerEnrollmentExchangeResponse
{
    public Guid RunnerId { get; set; }
    public Guid RunnerHostId { get; set; }
    public string RunnerName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string? ApplicationProfile { get; set; }
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public string InstallationId { get; set; } = string.Empty;
}

public sealed class InfrastructureRunnerRegistrationRequest
{
    public string InstallationId { get; set; } = string.Empty;
    public string RunnerName { get; set; } = string.Empty;
    public string? RunnerVersion { get; set; }
    public string? MachineName { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Architecture { get; set; }
    public string? DockerVersion { get; set; }
    public string? PublicIp { get; set; }
    public string? PrivateIp { get; set; }
    public string? Provider { get; set; }
    public string? Region { get; set; }
    public decimal? CpuCores { get; set; }
    public int? MemoryMb { get; set; }
    public int? DiskMb { get; set; }
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
}

public sealed class InfrastructureRunnerRegistrationResponse
{
    public Guid RunnerId { get; set; }
    public Guid RunnerHostId { get; set; }
    public string RunnerName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public string InstallationId { get; set; } = string.Empty;
}
