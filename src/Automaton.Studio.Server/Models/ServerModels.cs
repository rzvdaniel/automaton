namespace Automaton.Api.Entities;

public record FlowExecutionResult(IEnumerable<FlowExecution> FlowExecutions, int Total);