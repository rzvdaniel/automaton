using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>Short-lived, single-use possession challenge for committed-enrollment recovery.</summary>
public sealed class NodeEnrollmentRecoveryChallenge
{
    [Key]
    public Guid Id { get; set; }
    [Required] public Guid EnrollmentAuthorizationId { get; set; }
    public NodeEnrollmentAuthorization EnrollmentAuthorization { get; set; } = null!;
    [Required, MaxLength(32)] public byte[] Nonce { get; set; } = Array.Empty<byte>();
    [Required, MaxLength(64)] public string PublicKeySha256 { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
}
