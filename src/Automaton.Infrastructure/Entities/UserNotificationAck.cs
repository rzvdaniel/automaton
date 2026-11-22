namespace Automaton.Api.Entities;

/// <summary>A notice one person acknowledged. It comes back when its <c>Fingerprint</c> (what it is about) changes.</summary>
public class UserNotificationAck
{
    public Guid UserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public DateTime AcknowledgedAtUtc { get; set; }
}
