namespace Automaton.Api.Models;

/// <summary>Safety limits for user Action schedules. Plain server settings: they do not depend on who owns the schedule.</summary>
public sealed class ScheduleLimits
{
    public int MinimumIntervalSeconds { get; init; } = 300;
    public int MaximumConcurrentExecutions { get; init; } = 1;

    public static ScheduleLimits Default { get; } = new();

    /// <summary>Platform schedules (system ones and an app's home-card refresh) are not limited.</summary>
    public static ScheduleLimits Unlimited { get; } = new() { MinimumIntervalSeconds = 1, MaximumConcurrentExecutions = int.MaxValue };
}
