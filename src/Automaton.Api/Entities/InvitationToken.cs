using System.ComponentModel.DataAnnotations;

namespace Automaton.Api.Entities;

public class InvitationToken
{
    [Key]
    public Guid Id { get; set; }

    public string Token { get; set; } = string.Empty;

    public Guid ProjectId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public Guid InvitedBy { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool IsConsumed { get; set; }

    public DateTime CreatedAt { get; set; }
}