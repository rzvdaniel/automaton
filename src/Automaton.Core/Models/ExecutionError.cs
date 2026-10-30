namespace Automaton.Core.Models;

public class ExecutionError
{
    public DateTime ErrorTime { get; set; }

    public string WorkflowId { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
