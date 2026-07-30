using System.ComponentModel.DataAnnotations;
using Automaton.Api.Models;

namespace Automaton.Api.Entities;

/// <summary>Durable control-plane replacement lifecycle for one Infrastructure Runner.</summary>
public sealed class InfrastructureRunnerUpdateOperation
{
    [Key] public Guid Id { get; set; }
    public Guid RunnerHostId { get; set; }
    public Guid InfrastructureRunnerId { get; set; }
    public Guid RequestedByUserId { get; set; }
    [Required, MaxLength(40)] public string Status { get; set; } = InfrastructureRunnerUpdateOperationStatuses.Pending;
    [Required, MaxLength(160)] public string IdempotencyKey { get; set; } = string.Empty;
    [MaxLength(256)] public string? RequestedImage { get; set; }
    [MaxLength(256)] public string? TargetImageDigest { get; set; }
    [MaxLength(256)] public string? PreviousImageDigest { get; set; }
    public int? PreviousSpecificationVersion { get; set; }
    public int? TargetSpecificationVersion { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? ReconnectDeadlineUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    [MaxLength(2000)] public string? Error { get; set; }
    public string? Diagnostics { get; set; }
}
