using Automaton.Core.Models;

namespace Automaton.Core.Tests.Helpers
{
    public class TestWorkflowStep : WorkflowStep
    {
        protected override Task<ExecutionResult> RunAsync(StepExecutionContext context)
        {
            return Task.FromResult(new ExecutionResult());
        }
    }
}