using Automaton.Core.Models;

namespace Automaton.Core.Interfaces
{
    /// <summary>
    /// Extended interface for flow execution services that can work with different flow types
    /// </summary>
    /// <typeparam name="TFlow">The type of flow this service can execute</typeparam>
    public interface IFlowExecuteService<in TFlow> : IFlowExecuteService where TFlow : class
    {
        /// <summary>
        /// Executes a domain-specific flow
        /// </summary>
        /// <param name="flow">The flow to execute</param>
        /// <param name="executeDelay">Delay between step executions in milliseconds</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The workflow execution result</returns>
        Task<WorkflowExecution> Execute(TFlow flow, int executeDelay = 0, CancellationToken cancellationToken = default);
    }
}