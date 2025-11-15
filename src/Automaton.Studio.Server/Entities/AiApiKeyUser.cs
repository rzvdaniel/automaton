#nullable disable

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Links an AI API Key to a User for personal ownership.
/// </summary>
public class AiApiKeyUser
{
    public Guid UserId { get; set; }
    public Guid AiApiKeyId { get; set; }
}
