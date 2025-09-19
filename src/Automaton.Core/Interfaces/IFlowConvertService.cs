using Automaton.Core.Models;

namespace Automaton.Core.Interfaces;

/// <summary>
/// Interface for flow conversion services that convert domain-specific flow models to executable workflows
/// </summary>
/// <typeparam name="TFlow">The flow model type to convert from</typeparam>
public interface IFlowConvertService<in TFlow> where TFlow : class
{
    /// <summary>
    /// Converts a domain-specific flow model to an executable workflow
    /// </summary>
    /// <param name="flow">The flow to convert</param>
    /// <returns>An executable workflow</returns>
    Workflow ConvertFlow(TFlow flow);
}