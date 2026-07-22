using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public sealed class SecretDefinition
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(40)]
    public string OwnerType { get; set; } = string.Empty;

    public Guid OwnerId { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    public int CurrentVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAtUtc { get; set; }

    public ICollection<SecretVersion> Versions { get; set; } = new List<SecretVersion>();
}
