using Automaton.Api.Enums;
using Automaton.Core.Enums;

namespace Automaton.Api.Models;

public class RunnerDetails
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? ConnectionId { get; set; }
    public RunnerStatus Status { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public DateTime? LastConnectedAtUtc { get; set; }
    public DateTime? LastDisconnectedAtUtc { get; set; }
    public DateTime? CurrentSessionConnectedAtUtc { get; set; }
    public long TotalConnectedDurationSeconds { get; set; }
    public int ConnectionSessionCount { get; set; }
    public Guid? CurrentSessionId { get; set; }
    public string? LastDisconnectReason { get; set; }
    public string? Version { get; set; }
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }
    public string? ApiInstanceId { get; set; }
    public string? ApiColor { get; set; }
    public string Purpose { get; set; } = RunnerPurposes.Managed;
    public Guid? RunnerHostId { get; set; }
    public bool IsInfrastructureRunner => string.Equals(Purpose, RunnerPurposes.Infrastructure, StringComparison.OrdinalIgnoreCase);

    // Project context
    public AccessScope Scope { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public Guid? HostedRunnerId { get; set; }
    public bool IsHostedRunner { get; set; }
}
