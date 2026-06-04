using Automaton.Core.Enums;
using Automaton.Studio.Server.Enums;

namespace Automaton.Studio.Server.Models;

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
    public string? Version { get; set; }
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }

    // Project context
    public AccessScope Scope { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public Guid? HostedRunnerId { get; set; }
    public bool IsHostedRunner { get; set; }
}
