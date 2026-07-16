using System.ComponentModel.DataAnnotations;
using Automaton.Api.Models;

namespace Automaton.Api.Entities;

public class RunnerHostProvisioningOperation
{
    [Key]
    public Guid Id { get; set; }
    public Guid RunnerHostId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public virtual RunnerHost RunnerHost { get; set; } = null!;

    [Required, MaxLength(40)]
    public string Action { get; set; } = RunnerHostProvisioningActions.Create;
    [Required, MaxLength(40)]
    public string Status { get; set; } = RunnerHostProvisioningOperationStatuses.Pending;
    [Required, MaxLength(120)]
    public string IdempotencyKey { get; set; } = string.Empty;
    [MaxLength(120)]
    public string? ProviderResourceId { get; set; }
    public Guid? EnrollmentId { get; set; }
    [MaxLength(40)]
    public string? CloudInitTemplateVersion { get; set; }
    public DateTime? CloudInitGeneratedAtUtc { get; set; }
    [MaxLength(80)]
    public string? FailureCode { get; set; }
    [MaxLength(2000)]
    public string? Error { get; set; }
    public int Attempt { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime? NextTimeoutAtUtc { get; set; }
    public DateTime? ClaimedUntilUtc { get; set; }
}
