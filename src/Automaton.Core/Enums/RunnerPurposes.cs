namespace Automaton.Core.Enums;

/// <summary>The sole supported Runner purpose.</summary>
public static class RunnerPurposes
{
    public const string Managed = "Managed";

    public static string Normalize(string? purpose)
        => string.Equals(purpose, Managed, StringComparison.OrdinalIgnoreCase)
            ? Managed
            : throw new ArgumentException($"Unknown Runner purpose '{purpose}'.", nameof(purpose));
}
