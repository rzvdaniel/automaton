using Automaton.Core.Models;

namespace Automaton.Core.Interfaces
{
    /// <summary>
    /// Common interface for flow execution services with notification support
    /// </summary>
    public interface IFlowExecuteService
    {
        Task<WorkflowExecution> Execute(Workflow workflow, int executeDelay = 0, CancellationToken cancellationToken = default);
        Task<WorkflowExecution> Execute(Flow flow, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for services that can send real-time execution notifications
    /// </summary>
    public interface IExecutionNotificationService
    {
        Task SendLogUpdate(Guid workflowId, Guid executionId, string level, string message, 
            string? stepId = null, string? stepName = null, string? exception = null);
        Task SendStepExecution(string stepId);
        Task SendVariableUpdate(StepVariable variable);
    }
}
