namespace Automaton.Core.Models;

/// <summary>"Result ready" message on a Runner's result queue; carries identities only, never the result.</summary>
public sealed record RunnerExecutionResultReady(Guid OperationId, Guid RunnerId);
