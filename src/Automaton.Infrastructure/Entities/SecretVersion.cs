using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public sealed class SecretVersion
{
    [Key]
    public Guid Id { get; set; }

    public Guid SecretDefinitionId { get; set; }
    public SecretDefinition SecretDefinition { get; set; } = null!;

    public int Version { get; set; }

    [Required]
    public string ProtectedValue { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public ICollection<ExecutionSecretGrant> Grants { get; set; } = new List<ExecutionSecretGrant>();
}
