#nullable enable

using Automaton;

namespace Automaton.Api.Entities;

public class RunnerConnectionSession
{
    public Guid Id { get; set; }
    public Guid RunnerId { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; set; }
    public DateTime? DisconnectedAtUtc { get; set; }
    public string DisconnectReason { get; set; } = string.Empty;
    public string? ApiInstanceId { get; set; }
    public string? ApiColor { get; set; }

    public Runner Runner { get; set; } = null!;
}
