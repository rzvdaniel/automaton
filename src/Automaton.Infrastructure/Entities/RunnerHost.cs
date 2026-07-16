using Automaton.Api.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Api.Entities;

/// <summary>
/// A physical or virtual machine registered for infrastructure management and,
/// when enabled, managed user Runner containers.
/// </summary>
public class RunnerHost
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Provider-owned resource identifier, such as a Hetzner server ID.
    /// </summary>
    [MaxLength(120)]
    public string? ProviderResourceId { get; set; }

    [MaxLength(80)]
    public string? ProviderServerType { get; set; }

    [MaxLength(120)]
    public string? ProviderImageId { get; set; }

    [MaxLength(80)]
    public string? Region { get; set; }

    [MaxLength(120)]
    public string? PublicIp { get; set; }

    [MaxLength(120)]
    public string? PrivateIp { get; set; }

    [Required, MaxLength(40)]
    public string ProvisioningTransport { get; set; } = RunnerHostProvisioningTransports.InfrastructureRunner;

    [Required, MaxLength(40)]
    public string RuntimeConnectivityMode { get; set; } = RunnerHostRuntimeConnectivityModes.PublicHostPort;

    /// <summary>
    /// Whether managed user Runner containers may be placed on this host.
    /// This is independent from the local-vs-remote provisioning mechanism.
    /// </summary>
    public bool HostedRunnerSchedulingEnabled { get; set; } = true;

    [Required, MaxLength(40)]
    public string Status { get; set; } = RunnerHostStatuses.Active;

    [Required, MaxLength(40)]
    public string ProvisioningStatus { get; set; } = RunnerHostProvisioningStatuses.Manual;

    public DateTime? ProvisioningStartedAtUtc { get; set; }
    public DateTime? InfrastructureRunnerEnrolledAtUtc { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }

    [MaxLength(80)]
    public string? ProvisioningFailureCode { get; set; }

    [MaxLength(2000)]
    public string? ProvisioningFailureMessage { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal TotalCpuCores { get; set; } = 2m;

    [Column(TypeName = "decimal(5,2)")]
    public decimal ReservedCpuCores { get; set; }

    public int TotalMemoryMb { get; set; } = 4096;
    public int ReservedMemoryMb { get; set; }
    public int TotalDiskMb { get; set; } = 40960;
    public int ReservedDiskMb { get; set; }

    public DateTime? LastHeartbeatAtUtc { get; set; }

    /// <summary>
    /// Runner-reported facts that are useful to display but not to schedule on.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Latest host-validation snapshot, kept separate from relatively stable metadata.
    /// </summary>
    public string? HealthJson { get; set; }

    /// <summary>
    /// Shared Runner installed with infrastructure privileges on this host.
    /// </summary>
    public Guid? InfrastructureRunnerId { get; set; }
    public virtual Runner? InfrastructureRunner { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<HostedRunner> HostedRunners { get; set; } = new List<HostedRunner>();
    public virtual ICollection<RunnerEnrollment> RunnerEnrollments { get; set; } = new List<RunnerEnrollment>();

    [NotMapped]
    public bool IsSchedulingEligible =>
        ProvisioningStatus == RunnerHostProvisioningStatuses.Ready &&
        Status == RunnerHostStatuses.Active &&
        HostedRunnerSchedulingEnabled &&
        ValidatedAtUtc.HasValue &&
        (ProvisioningTransport == RunnerHostProvisioningTransports.LocalDocker
            || InfrastructureRunnerId.HasValue);
}
