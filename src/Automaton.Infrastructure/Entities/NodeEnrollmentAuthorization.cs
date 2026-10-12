using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>Pre-authorized, one-use Node bootstrap record and the committed identity it binds.</summary>
public sealed class NodeEnrollmentAuthorization
{
    [Key]
    public Guid Id { get; set; }
    [Required] public Guid NodeId { get; set; }
    [Required] public Guid InstallationId { get; set; }
    [Required] public Guid ControlPlaneRealmId { get; set; }
    [Required, MaxLength(128)] public string OwnerScope { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string PinnedPublicKeySha256 { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string EnrollmentTokenSha256 { get; set; } = string.Empty;
    [Required, MaxLength(24)] public string Status { get; set; } = NodeEnrollmentStatuses.Pending;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CommittedAtUtc { get; set; }
    public byte[]? CertificateDer { get; set; }
    [MaxLength(64)] public string? CertificateSha256 { get; set; }
    [MaxLength(64)] public string? NodeApiKeySha256 { get; set; }
    public byte[]? ProtectedCredentialBundle { get; set; }
    public DateTime? CredentialBundleExpiresAtUtc { get; set; }
    public ICollection<NodeEnrollmentRecoveryChallenge> RecoveryChallenges { get; set; } = new List<NodeEnrollmentRecoveryChallenge>();
}

public static class NodeEnrollmentStatuses
{
    public const string Pending = "Pending";
    public const string Committed = "Committed";
    public const string Revoked = "Revoked";
}
