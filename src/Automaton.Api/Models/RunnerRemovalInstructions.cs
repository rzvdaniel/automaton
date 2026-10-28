namespace Automaton.Api.Models;

public sealed record RunnerRemovalInstructions(Guid RunnerId, string UninstallCommand);
