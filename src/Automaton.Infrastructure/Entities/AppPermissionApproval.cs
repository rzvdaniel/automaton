using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>An app's current approval state for one requested platform permission.</summary>
public sealed class AppPermissionApproval
{
    public const string BackupsRead = "backups:read";
    public const string CredentialVariable = "APP_AUTOMATON_API_KEY";

    [Key]
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    [Required, MaxLength(100)]
    public string Permission { get; set; } = string.Empty;
    [Required, MaxLength(20)]
    public string Status { get; set; } = AppPermissionStatuses.Pending;
    public string? ProtectedKey { get; set; }
    [MaxLength(64)]
    public string? TokenHash { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public static class AppPermissionStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Revoked = "Revoked";
}
