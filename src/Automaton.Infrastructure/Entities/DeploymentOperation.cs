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

    public const int ErrorMessageMaxLength = 4000;

    [MaxLength(ErrorMessageMaxLength)]
    public string? ErrorMessage { get; set; }

    /// <summary>Fits an error into the column, keeping the start and the end of a long message.</summary>
    public static string? FitErrorMessage(string? error)
    {
        if (error is null || error.Length <= ErrorMessageMaxLength) return error;
        const string note = "\n... [error cut to fit; start and end kept] ...\n";
        var keep = ErrorMessageMaxLength - note.Length;
        var head = keep / 4;
        return error[..head] + note + error[^(keep - head)..];
    }

    public string? ResultJson { get; set; }
}

public static class DeploymentOperationStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}
