namespace Automaton.Core.Enums;

/// <summary>
/// Describes the trust profile of a Runner installation. The purpose is
/// metadata; actual privileges come from the container/host installation.
/// </summary>
public static class RunnerPurposes
{
    public const string Managed = "Managed";
    public const string Infrastructure = "Infrastructure";

    public static string Normalize(string? purpose)
        => string.Equals(purpose, Infrastructure, StringComparison.OrdinalIgnoreCase)
            ? Infrastructure
            : Managed;
}
