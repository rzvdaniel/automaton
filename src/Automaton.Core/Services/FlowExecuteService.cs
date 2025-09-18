using Automaton.Core.Interfaces;

namespace Automaton.Core.Services;

public class FlowExecuteService : BaseFlowExecuteService
{
    public FlowExecuteService(
        FlowConvertService flowConvertService, 
        IExecutionNotificationService notificationService = null)
        : base(flowConvertService, notificationService ?? new NullExecutionNotificationService(), string.Empty)
    {
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

    public Task SendVariableUpdate(Core.Models.StepVariable variable)
    {
        return Task.CompletedTask;
    }
}
