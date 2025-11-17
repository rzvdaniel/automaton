using Automaton.Core.Models;

namespace Automaton.Runner.Console.Models;

/// <summary>
/// Payload for custom step execution (subset of CustomStep model)
/// </summary>
public class CustomStepPayload
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Code { get; set; }
    public string Language { get; set; }
    public List<StepVariable> CodeInputVariables { get; set; }
    public List<StepVariable> CodeOutputVariables { get; set; }
}
