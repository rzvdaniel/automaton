using MediatR;
using System.Text.Json.Serialization;

namespace Automaton.Api.Models
{
    public class ExecuteFlowDetails : IRequest
    {
        [JsonRequired]
        public Guid FlowId { get; set; }
        public IEnumerable<Guid> RunnerIds { get; set; } = Array.Empty<Guid>();
    }
}
