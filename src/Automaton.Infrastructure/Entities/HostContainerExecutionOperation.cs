using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>Durable request and result state for one trusted host-container operation.</summary>
public sealed class HostContainerExecutionOperation
{
    [Key]
    public Guid Id { get; set; }

    public Guid HostedRunnerId { get; set; }
    public Guid RunnerHostId { get; set; }
    public Guid InfrastructureRunnerId { get; set; }
    public Guid AppId { get; set; }
    public Guid DeploymentTargetId { get; set; }

    [Required, MaxLength(32)]
    public string Status { get; set; } = HostContainerExecutionOperationStatuses.Queued;

    [Required, MaxLength(64)]
    public string RequestHash { get; set; } = null!;

    public string? ResultJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

public static class HostContainerExecutionOperationStatuses
{
    public const string Queued = "Queued";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
