#nullable disable

using Automaton;
using Automaton.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class Runner
{
    [Required]
    public Guid Id { get; set; }
    [Required]
    public string Name { get; set; }
    public string ConnectionId { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public DateTime? LastConnectedAtUtc { get; set; }
    public DateTime? LastDisconnectedAtUtc { get; set; }
    public string Status { get; set; }
    public string? Version { get; set; }
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }
    public string? ApiInstanceId { get; set; }
    public string? ApiColor { get; set; }

    /// <summary>
    /// Installation purpose. This does not grant privileges; the installation
    /// profile does that. Infrastructure runners are administrator-owned.
    /// </summary>
    [Required, MaxLength(40)]
    public string Purpose { get; set; } = RunnerPurposes.Managed;

    /// <summary>
    /// JSON-serialized Runner capabilities reported by the installation.
    /// </summary>
    public string CapabilitiesJson { get; set; } = "[]";

    /// <summary>
    /// Stable identity for one installed Runner volume.
    /// </summary>
    [MaxLength(128)]
    public string? InstallationId { get; set; }

    public Guid? ApiKeyId { get; set; }
    public virtual ApiKey? ApiKey { get; set; }

    /// <summary>
    /// JSON-serialized script engine configuration settings
    /// Stores RunnerScriptEngineSettings as JSON string
    /// </summary>
    public string? ScriptEngineSettings { get; set; }

    public virtual IEnumerable<RunnerUser> RunnerUsers { get; set; }

    // Project linking
    public virtual ICollection<ProjectRunner> ProjectRunners { get; set; } = new List<ProjectRunner>();
}
