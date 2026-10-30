#nullable disable

using Automaton;
using System.Text.Json.Serialization;

namespace Automaton.Api.Models
{
    public class FlowExecution
    {
        [JsonRequired]
        public Guid Id { get; set; }
        [JsonRequired]
        public Guid FlowId { get; set; }
        [JsonRequired]
        public DateTime Started { get; set; }
        [JsonRequired]
        public DateTime Finished { get; set; }
        public string Status { get; set; }
        public string Application { get; set; }
        public List<FlowExecutionLogEntry> Logs { get; set; } = new();
    }
}
