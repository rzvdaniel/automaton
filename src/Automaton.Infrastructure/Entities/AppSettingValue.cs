using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

/// <summary>
/// The plain value an owner chose for one setting an app declares. Unlike secrets, these values can be shown
/// again; they are never stored in the secret store, and the secret store is never read back for display.
/// </summary>
public sealed class AppSettingValue
{
    [Key]
    public Guid Id { get; set; }
    public Guid AppId { get; set; }
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;
    [Required]
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}
