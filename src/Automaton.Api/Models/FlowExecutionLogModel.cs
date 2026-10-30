namespace Automaton.Api.Models
{
    public class FlowExecutionLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public string? StepId { get; set; }
        public string? StepName { get; set; }
    }
}
