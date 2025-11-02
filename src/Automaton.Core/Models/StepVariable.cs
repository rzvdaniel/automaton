namespace Automaton.Core.Models;

public class StepVariable
{
    public string? Id { get; set; }
    public required string? Name { get; set; }
    public virtual string? Value { get; set; }
    public string? Description { get; set; }
}
