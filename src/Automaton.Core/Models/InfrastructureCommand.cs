namespace Automaton.Core.Models;

/// <summary>
/// A short-lived command sent by the API to an administrator-owned
/// infrastructure Runner. The Runner installation profile, not this payload,
/// determines whether the command can reach the host.
/// </summary>
public sealed class InfrastructureCommandRequest
{
    public string Script { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 300;
}

public sealed class InfrastructureCommandResponse
{
    public bool Success { get; init; }
    public int ExitStatus { get; init; }
    public string Output { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}
