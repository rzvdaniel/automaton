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
        IExecutionNotificationService notificationService = null)
        : base(flowConvertService, 
               notificationService ?? new NullExecutionNotificationService(), 
               string.Empty)
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
}

/// <summary>
/// Null implementation for when no notifications are needed
/// </summary>
public class NullExecutionNotificationService : IExecutionNotificationService
{
    public Task SendLogUpdate(Guid workflowId, Guid executionId, string level, string message, 
        string stepId = null, string stepName = null, string exception = null)
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
