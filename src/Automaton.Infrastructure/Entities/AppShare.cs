namespace Automaton.Api.Entities;

/// <summary>A person the app's owner chose to share the app with: they can open it, nothing more.</summary>
public class AppShare
{
    public Guid WebAppId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
