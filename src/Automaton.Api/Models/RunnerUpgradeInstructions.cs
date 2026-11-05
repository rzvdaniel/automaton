namespace Automaton.Api.Models;

public sealed record RunnerUpgradeInstructions(Guid RunnerId, string Image, bool UpToDate, string UpgradeCommand);

public static class RunnerUpgradeStates
{
    public const string InProgress = "InProgress";
    public const string Reconnected = "Reconnected";
    public const string RolledBack = "RolledBack";
    public const string Unknown = "Unknown";
}

public sealed record RunnerUpgradeStatus(Guid RunnerId, string State, string Message, string? ImageDigest);
