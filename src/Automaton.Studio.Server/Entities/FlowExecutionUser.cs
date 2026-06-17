#nullable disable

using Automaton;

namespace Automaton.Api.Entities;

public class FlowExecutionUser
{
    public Guid UserId { get; set; }
    public Guid FlowExecutionId { get; set; }
}
