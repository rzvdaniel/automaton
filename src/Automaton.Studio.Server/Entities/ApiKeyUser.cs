#nullable disable

namespace Automaton.Studio.Server.Entities;

/// <summary>
/// Links an API Key to a User for personal ownership.
/// </summary>
public class ApiKeyUser
{
    public Guid UserId { get; set; }
    public Guid ApiKeyId { get; set; }
}
