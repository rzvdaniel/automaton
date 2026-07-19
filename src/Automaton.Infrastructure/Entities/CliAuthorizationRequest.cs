using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class CliAuthorizationRequest
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(32)]
    public string UserCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string HashedVerifier { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string DeviceName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? RemoteIp { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    [MaxLength(1000)]
    public string? EncryptedApiKey { get; set; }
    public DateTime? CredentialIssuedAt { get; set; }
    public Guid? ApiKeyId { get; set; }
    public Guid? UserId { get; set; }
}
