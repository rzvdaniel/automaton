using System.ComponentModel.DataAnnotations;
using Automaton.Api.Models;

namespace Automaton.Api.Entities;

/// <summary>
/// One-time authorization to install an administrator-approved Runner profile
/// on a specific Runner Host.
/// </summary>
public class RunnerEnrollment
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid RunnerHostId { get; set; }
    public virtual RunnerHost RunnerHost { get; set; } = null!;

    public Guid? HostedRunnerId { get; set; }
    public Guid? OperationId { get; set; }
    public virtual HostedRunnerDeployment? Operation { get; set; }
    public Guid? SecretVersionId { get; set; }

    [Required]
    public Guid RequestedByUserId { get; set; }

    [Required, MaxLength(40)]
    public string ExpectedPurpose { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string ExpectedCapabilitiesJson { get; set; } = "[]";

    [Required, MaxLength(128)]
    public string EnrollmentTokenHash { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string InstallationId { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Status { get; set; } = RunnerEnrollmentStatuses.Pending;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }
    public Guid? RunnerId { get; set; }

    [MaxLength(1000)]
    public string? FailureReason { get; set; }
}
