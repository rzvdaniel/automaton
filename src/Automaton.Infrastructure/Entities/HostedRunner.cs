using Automaton.Api.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Automaton.Api.Entities;

/// <summary>
/// A managed runner container allocated to one paid user.
/// </summary>
public class HostedRunner
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public virtual ApplicationUser User { get; set; } = null!;

    public Guid? RunnerHostId { get; set; }
    public virtual RunnerHost? RunnerHost { get; set; }

    public Guid? RunnerId { get; set; }
    public virtual Runner? Runner { get; set; }

    public Guid? ApiKeyId { get; set; }
    public virtual ApiKey? ApiKey { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Status { get; set; } = HostedRunnerStatuses.Pending;

    public Guid HostedRunnerSpecificationId { get; set; }
    public virtual HostedRunnerSpecification HostedRunnerSpecification { get; set; } = null!;
    public int SpecificationRevision { get; set; }
    public HostedRunnerSpecificationSnapshot SpecificationSnapshot { get; set; } = new();
    public HostedRunnerSpecificationSnapshot? PendingSpecificationSnapshot { get; set; }

    [Required, MaxLength(160)]
    public string ContainerName { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? ContainerId { get; set; }

    [MaxLength(2000)]
    public string? RuntimeEndpoint { get; set; }

    /// <summary>
    /// Host port published for the Runner gateway when this Runner is remote.
    /// Same-host Docker deployments use the container network and leave this null.
    /// </summary>
    public int? RuntimePort { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ProvisionedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public long StorageAllowanceBytes { get; set; }
    /// <summary>
    /// Compatibility snapshot of the account-owned XFS project identity.
    /// This is host metadata, not user input; allocation authority is held by
    /// AccountStorageAllocation.
    /// </summary>
    public int? StorageProjectId { get; set; }
    public DateTime? StorageMeasuredAtUtc { get; set; }
    public DateTime? StorageReceivedAtUtc { get; set; }
    [MaxLength(40)] public string StorageMeasurementSource { get; set; } = StorageMeasurementSources.RunnerSelfReported;
    [MaxLength(32)] public string StorageMeasurementStatus { get; set; } = StorageMeasurementStatuses.NeverMeasured;
    public long? StorageSystemBytes { get; set; }
    public long? StorageUserBytes { get; set; }
    public long? StorageTotalBytes { get; set; }
    [Column(TypeName = "decimal(7,2)")] public decimal? StorageUsagePercent { get; set; }
    [MaxLength(32)] public string StorageState { get; set; } = HostedRunnerStorageStates.Unknown;
    [MaxLength(2000)] public string? StorageMeasurementError { get; set; }
    public DateTime? StorageVerificationRequestedAtUtc { get; set; }
    public DateTime? StorageVerifiedAtUtc { get; set; }
    public long? StorageVerifiedSystemBytes { get; set; }
    public long? StorageVerifiedUserBytes { get; set; }
    public long? StorageVerifiedTotalBytes { get; set; }
    public DateTime? StorageSelfReportedAtUtc { get; set; }
    public long? StorageSelfReportedSystemBytes { get; set; }
    public long? StorageSelfReportedUserBytes { get; set; }
    public long? StorageSelfReportedTotalBytes { get; set; }
    [MaxLength(32)] public string StorageSelfReportedStatus { get; set; } = StorageMeasurementStatuses.NeverMeasured;
    [MaxLength(2000)] public string? StorageSelfReportedError { get; set; }

    [MaxLength(32)] public string EnforcementState { get; set; } = HostedRunnerEnforcementStates.None;
    public DateTime? OverLimitSinceUtc { get; set; }
    public DateTime? GracePeriodEndsAtUtc { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }
    public DateTime? RecoveredAtUtc { get; set; }
    [MaxLength(2000)] public string? LastEnforcementReason { get; set; }
    public DateTime? LastNotificationAtUtc { get; set; }

    public virtual ICollection<HostedRunnerDeployment> Deployments { get; set; } = new List<HostedRunnerDeployment>();
    public virtual ICollection<HostedRunnerStorageObservation> StorageObservations { get; set; } = new List<HostedRunnerStorageObservation>();
    public virtual ICollection<HostedRunnerStorageVerification> StorageVerifications { get; set; } = new List<HostedRunnerStorageVerification>();
}
