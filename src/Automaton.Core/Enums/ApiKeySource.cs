namespace Automaton.Core.Enums;

/// <summary>
/// Identifies how an API key was created and whether it is managed by a user or the platform.
/// </summary>
public enum ApiKeySource
{
    Manual = 1,
    CliAuthorization = 2,
    RunnerProvisioning = 3
}
