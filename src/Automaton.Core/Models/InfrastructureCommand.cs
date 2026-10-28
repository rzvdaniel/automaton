namespace Automaton.Core.Models;

/// <summary>Result returned by the bounded Machine Management host command executor.</summary>
public sealed class InfrastructureCommandResponse
{
    public bool Success { get; init; }
    public int ExitStatus { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public bool OutputExceededLimit { get; init; }
    public bool ErrorTruncated { get; init; }
}
