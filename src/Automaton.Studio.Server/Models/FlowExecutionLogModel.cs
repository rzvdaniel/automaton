namespace Automaton.Studio.Server.Models
{
    public class FlowExecutionLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Exception { get; set; }
        public string StepId { get; set; }
        public string StepName { get; set; }
    }
}