using System;

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Junction table linking AI chat sessions to users
/// </summary>
public class AiChatSessionUser
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }

    // Navigation properties
    public virtual AiChatSession Session { get; set; } = null!;
    public virtual ApplicationUser User { get; set; } = null!;
}
