using Automaton.Core.Enums;

namespace Automaton.Api.Models;

public sealed record RunnerEnrollmentCreatedResponse
{
    public Guid Id { get; init; }
    public Guid RunnerHostId { get; init; }
    public string EnrollmentToken { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public string ExpectedPurpose { get; init; } = RunnerPurposes.Infrastructure;
    public IReadOnlyList<string> ExpectedCapabilities { get; init; } = Array.Empty<string>();
    public string DockerImage { get; init; } = "automaton-runner:latest";
}

public sealed record HostedRunnerEnrollmentMaterial(
    Guid EnrollmentId,
    Guid SecretGrantId,
    DateTime ExpiresAtUtc);

public sealed record RunnerEnrollmentExchangeRequest
{
    public string EnrollmentToken { get; init; } = string.Empty;
    public string? RunnerName { get; init; }
    public string? RunnerVersion { get; init; }
    public string? MachineName { get; init; }
    public string? OperatingSystem { get; init; }
    public string? Architecture { get; init; }
    public string? ImageVersion { get; init; }
}

public sealed record RunnerEnrollmentExchangeResponse
{
    public Guid RunnerId { get; init; }
    public Guid RunnerHostId { get; init; }
    public string RunnerName { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Purpose { get; init; } = RunnerPurposes.Infrastructure;
    public string? ApplicationProfile { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public string InstallationId { get; init; } = string.Empty;
}
