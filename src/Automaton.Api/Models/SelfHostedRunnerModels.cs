using Automaton.Core.Enums;

namespace Automaton.Api.Models;

public sealed record CreateSelfHostedRunnerInstallationRequest
{
    public string RunnerName { get; init; } = string.Empty;
    public string ApplicationProfile { get; init; } = RunnerApplicationProfiles.MachineManagement;
    public bool AcknowledgeHostAccess { get; init; } = false;
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
    public string Status { get; init; } = SelfHostedRunnerInstallationStatuses.CommandCreated;
}

public sealed record SelfHostedRunnerInstallationStatusResponse
{
    public string InstallationId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }
    public Guid? RunnerId { get; init; }
    public DateTime? LastConnectedAtUtc { get; init; }
    public DateTime? LastHeartbeatAtUtc { get; init; }
}

public static class SelfHostedRunnerInstallationStatuses
{
    public const string CommandCreated = "CommandCreated";
    public const string WaitingForEnrollment = "WaitingForEnrollment";
    public const string RunnerEnrolled = "RunnerEnrolled";
    public const string RunnerConnected = "RunnerConnected";
    public const string Expired = "Expired";
    public const string OfflineAfterEnrollment = "OfflineAfterEnrollment";
}
