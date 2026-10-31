namespace Automaton.Api.Models;

public sealed record RunnerUpgradeInstructions(Guid RunnerId, string Image, bool UpToDate, string UpgradeCommand);
