using Automaton.Core.Models;

namespace Automaton.Steps;

public class EmitLog : WorkflowStep
{
    public string Message { get; set; } = string.Empty;

    protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
    {
        logger.Debug("Write message: {0}", Message);

        return Task.FromResult(ExecutionResult.Next());
    }
}
