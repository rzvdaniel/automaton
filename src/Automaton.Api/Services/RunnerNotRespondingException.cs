namespace Automaton.Api.Services;

/// <summary>A Runner did not answer a command in time, usually because it is offline or restarting.</summary>
public sealed class RunnerNotRespondingException(string message) : Exception(message);
