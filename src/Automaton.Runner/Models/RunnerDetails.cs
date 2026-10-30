using Automaton.Core.Enums;

namespace Automaton.Runner.Console.Models;

public class RunnerDetails
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public RunnerStatus Status { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public DateTime? LastConnectedAtUtc { get; set; }
    public DateTime? LastDisconnectedAtUtc { get; set; }
    public string? Version { get; set; }
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }
    public string? ApiInstanceId { get; set; }
    public string? ApiColor { get; set; }
    public string? ImageDigest { get; set; }
    public int? ContainerSpecificationVersion { get; set; }
    public string? ApiCompatibilityVersion { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Architecture { get; set; }
    public string? ContainerHealth { get; set; }
    public string Purpose { get; set; } = RunnerPurposes.Managed;
    public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    public string? InstallationId { get; set; }
}
