using Automaton.Core.Models;

namespace Automaton.Core.Interfaces;

/// <summary>
/// Interface for persisting workflow execution results
/// </summary>
public interface IWorkflowExecutionPersistenceService
{
    /// <summary>
    /// Saves workflow execution results
    /// </summary>
    /// <param name="workflowExecution">The workflow execution to save</param>
    /// <returns>Task representing the async operation</returns>
    Task SaveWorkflowExecutionAsync(WorkflowExecution workflowExecution);
}

/// <summary>
/// Null implementation that does nothing - useful for scenarios where persistence is not needed
/// </summary>
public class NullWorkflowExecutionPersistenceService : IWorkflowExecutionPersistenceService
{
    public Task SaveWorkflowExecutionAsync(WorkflowExecution workflowExecution)
    {
        return Task.CompletedTask;
    }
}