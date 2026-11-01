namespace Automaton.Runner.Console.Services;

/// <summary>
/// Stepped retry delays shared by the Runner's retry loops: quick first retries for brief outages,
/// then slower ones up to a ceiling, so a persistent failure does not hammer the server.
/// </summary>
internal static class RetrySchedule
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)
    ];

    /// <summary>Ceiling for retries inside one API request, which is bounded by its operation deadline.</summary>
    public static readonly TimeSpan RequestCeiling = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling for background retries that continue until they succeed or the Runner stops.</summary>
    public static readonly TimeSpan BackgroundCeiling = TimeSpan.FromMinutes(5);

    /// <summary>The delay before retry <paramref name="attempt"/> (0-based), plus up to 10% jitter.</summary>
    public static TimeSpan Delay(int attempt, TimeSpan ceiling, double jitter = 0)
    {
        var step = Steps[Math.Clamp(attempt, 0, Steps.Length - 1)];
        if (step > ceiling) step = ceiling;
        return step + TimeSpan.FromMilliseconds(Math.Ceiling(step.TotalMilliseconds * 0.1 * Math.Clamp(jitter, 0, 1)));
    }
}
