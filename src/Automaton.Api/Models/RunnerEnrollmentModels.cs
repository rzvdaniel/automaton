namespace Automaton.Api.Models;

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
    public string RunnerName { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Purpose { get; init; } = "Managed";
    public string? ApplicationProfile { get; init; }
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
    public string InstallationId { get; init; } = string.Empty;
}
