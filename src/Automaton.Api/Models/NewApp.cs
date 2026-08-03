namespace Automaton.Api.Models;

public class NewApp
{
    public string Name { get; set; }
    public string? Tags { get; set; }
    public Guid? ExecutionRunnerId { get; set; }
    public bool PrivilegedHostAccessAcknowledged { get; set; }
}
