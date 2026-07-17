using Automaton.Core.Enums;

namespace Automaton.Api.Models;

public sealed class InfrastructureRunnerRegistrationRequest
{
    public string InstallationId { get; init; } = string.Empty;
    public string RunnerName { get; init; } = string.Empty;
    public string? RunnerVersion { get; init; }
    public string? MachineName { get; init; }
    public string? OperatingSystem { get; init; }
    public string? Architecture { get; init; }
    public string? DockerVersion { get; init; }
    public string? PublicIp { get; init; }
    public string? PrivateIp { get; init; }
    public string? Provider { get; init; }
    public string? Region { get; init; }
    public decimal? CpuCores { get; init; }
    public int? MemoryMb { get; init; }
    public int? DiskMb { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
}

public sealed class InfrastructureRunnerRegistrationResponse
{
    public Guid RunnerId { get; init; }
    public Guid RunnerHostId { get; init; }
    public string RunnerName { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Purpose { get; init; } = RunnerPurposes.Infrastructure;
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public string InstallationId { get; init; } = string.Empty;
}

public sealed class InfrastructureRunnerEnrollmentKeyResponse
{
    public Guid Id { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string KeyPrefix { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UsedAtUtc { get; init; }
    public int UseCount { get; init; }
    public int MaximumUses { get; init; } = 1;
    public bool IsActive { get; init; }
    public string Status { get; init; } = "Pending";
}
