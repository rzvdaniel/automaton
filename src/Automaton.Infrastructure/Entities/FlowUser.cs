#nullable disable

using Automaton;

namespace Automaton.Api.Entities;

public class FlowUser
{
    public Guid UserId { get; set; }
    public Guid FlowId { get; set; }
}
