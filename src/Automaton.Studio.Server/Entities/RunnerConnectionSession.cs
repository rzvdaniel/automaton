#nullable disable

namespace Automaton.Studio.Server.Entities;

public class RunnerConnectionSession
{
    public Guid Id { get; set; }
    public Guid RunnerId { get; set; }
    public string ConnectionId { get; set; }
    public DateTime ConnectedAtUtc { get; set; }
    public DateTime? DisconnectedAtUtc { get; set; }
    public string DisconnectReason { get; set; }
    public string? ApiInstanceId { get; set; }
    public string? ApiColor { get; set; }

    public Runner Runner { get; set; }
}
