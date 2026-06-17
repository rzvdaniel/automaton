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

    [Required, MaxLength(40)]
    public string Action { get; set; } = HostedRunnerDeploymentActions.Provision;

    [Required, MaxLength(40)]
    public string Status { get; set; } = HostedRunnerDeploymentStatuses.Pending;

    public string? CommandPreview { get; set; }
    public string? Logs { get; set; }
    public string? Error { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
}
