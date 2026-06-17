using MediatR;

namespace Automaton.Api.Models
{
    public class ExecuteFlowDetails : IRequest
    {
        public Guid FlowId { get; set; }
        public IEnumerable<Guid> RunnerIds { get; set; }
    }
}