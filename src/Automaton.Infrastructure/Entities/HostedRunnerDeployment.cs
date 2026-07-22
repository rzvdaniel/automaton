using Automaton.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>
/// One provisioning/deprovisioning attempt for a hosted runner.
/// </summary>
public class HostedRunnerDeployment
{
    [Key]
    public Guid Id { get; set; }

    public Guid HostedRunnerId { get; set; }
    public virtual HostedRunner HostedRunner { get; set; } = null!;
    public virtual AppModuleExecution? AppModuleExecution { get; set; }

    [Required, MaxLength(40)]
    public string Action { get; set; } = HostedRunnerDeploymentActions.Provision;

    [Required, MaxLength(40)]
    public string Status { get; set; } = HostedRunnerDeploymentStatuses.Pending;

    public string? CommandPreview { get; set; }
    public string? Logs { get; set; }
    public string? Error { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RegistrationDeadlineUtc { get; set; }
    public Guid? ExpectedRunnerId { get; set; }
    [MaxLength(120)]
    public string? PreviousConnectionId { get; set; }
    public DateTime? ReconnectAfterUtc { get; set; }
    public DateTime? ReconnectDeadlineUtc { get; set; }
    public DateTime? LastRecoveryAttemptUtc { get; set; }
    public DateTime? FinishedAt { get; set; }
}
