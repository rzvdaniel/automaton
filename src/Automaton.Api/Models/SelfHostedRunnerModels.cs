using Automaton.Core.Enums;

namespace Automaton.Api.Models;

public sealed record CreateSelfHostedRunnerInstallationRequest
{
    public string RunnerName { get; init; } = string.Empty;
    public string ApplicationProfile { get; init; } = RunnerApplicationProfiles.Standard;
}

public sealed record SelfHostedRunnerInstallationResponse
{
    public string InstallationId { get; init; } = string.Empty;
    public Guid EnrollmentId { get; init; }
    public string RunnerName { get; init; } = string.Empty;
    public string ApplicationProfile { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public string InstallationCommand { get; init; } = string.Empty;
    public bool RequiresHostAccessAcknowledgement { get; init; }
    public string? HostAccessWarning { get; init; }
}
