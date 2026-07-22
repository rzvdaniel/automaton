using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public sealed class ExecutionSecretGrant
{
    [Key]
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }
    public Guid SecretVersionId { get; set; }
    public SecretVersion SecretVersion { get; set; } = null!;
    public Guid AppId { get; set; }

    [Required, MaxLength(64)]
    public string ActionKey { get; set; } = string.Empty;

    public Guid RunnerId { get; set; }
    public Guid? RunnerHostId { get; set; }

    [Required, MaxLength(40)]
    public string DeliveryType { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string TargetName { get; set; } = string.Empty;

    public int MaximumReads { get; set; } = 1;
    public int ReadCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? LeasedAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}
