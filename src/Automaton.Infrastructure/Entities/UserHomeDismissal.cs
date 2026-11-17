namespace Automaton.Api.Entities;

/// <summary>An optional Needs-you card one person dismissed for good.</summary>
public class UserHomeDismissal
{
    public Guid UserId { get; set; }
    public string CardKey { get; set; } = string.Empty;
    public DateTime DismissedAtUtc { get; set; }
}
