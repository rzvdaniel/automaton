using Automaton.Core.Models;

namespace Automaton.Runner.Console.Models;

/// <summary>
/// Payload for custom step execution (subset of CustomStep model)
/// </summary>
public class CustomStepPayload
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<StepVariable> CodeInputVariables { get; set; } = [];
    public List<StepVariable> CodeOutputVariables { get; set; } = [];
}
