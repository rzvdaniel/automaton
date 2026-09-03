using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>
/// Durable state for a package deployment accepted by the API.
/// The package remains available to the server-owned executor until the
/// operation reaches a terminal state.
/// </summary>
public sealed class DeploymentOperation
{
    [Key]
    public Guid Id { get; set; }

    public Guid WebAppId { get; set; }
    public Guid OwnerUserId { get; set; }

    [MaxLength(120)]
    public string? TargetSlug { get; set; }

    [Required, MaxLength(32)]
    public string Status { get; set; } = DeploymentOperationStatuses.Pending;

    [Required, MaxLength(1024)]
    public string PackagePath { get; set; } = null!;

    public bool Clean { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    [MaxLength(120)]
    public string? ExecutorInstanceId { get; set; }
    public Guid? ExecutionAttemptId { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(4000)]
    public string? ErrorMessage { get; set; }

    public string? ResultJson { get; set; }
}

public static class DeploymentOperationStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
