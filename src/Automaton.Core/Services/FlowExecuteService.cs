using Automaton.Core.Interfaces;
using Automaton.Core.Models;

namespace Automaton.Core.Services;

/// <summary>
/// Flow execution service for console runners that works with Flow objects
/// </summary>
public class FlowExecuteService : BaseFlowExecuteService<Flow>
{
    public FlowExecuteService(
        FlowConvertService flowConvertService, 
        string applicationName,
        IExecutionNotificationService? notificationService = null,
        IWorkflowExecutionPersistenceService? persistenceService = null)
        : base(flowConvertService, 
               notificationService ?? new NullExecutionNotificationService(), 
               applicationName,
               persistenceService ?? new NullWorkflowExecutionPersistenceService())
    {
    }

    /// <summary>
    /// Override to support direct Flow execution for backward compatibility
    /// This resolves ambiguity by explicitly implementing the Flow overload
    /// </summary>
    public override async Task<WorkflowExecution> Execute(Flow flow, CancellationToken cancellationToken = default)
    {
        // Use the typed execute method with 0 delay
        return await Execute(flow, 0, cancellationToken);
    }

    /// <summary>
    /// Runner-specific post-execution processing override
    /// Note: Execution persistence is handled by the base class automatically if a persistence service is provided
    /// This method is available for any additional runner-specific processing if needed in the future
    /// </summary>
    protected override async Task OnExecutionCompleted(WorkflowExecution workflowExecution)
    {
        // Execution is saved by the base class if a persistence service was provided
        // This method is left available for future runner-specific post-processing needs
        await base.OnExecutionCompleted(workflowExecution);
    }
}

/// <summary>
/// Null implementation for when no notifications are needed
/// </summary>
public class NullExecutionNotificationService : IExecutionNotificationService
{
    public Task SendLogUpdate(Guid workflowId, Guid executionId, string level, string message, 
        string? stepId = null, string? stepName = null, string? exception = null)
    {
        return Task.CompletedTask;
    }

    public Task SendStepExecution(string stepId)
    {
        return Task.CompletedTask;
    }

    public Task SendVariableUpdate(StepVariable variable)
    {
        return Task.CompletedTask;
    }
}
