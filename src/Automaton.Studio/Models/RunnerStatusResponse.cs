namespace Automaton.Studio.Models;

/// <summary>
/// Client-side model for runner status response
/// </summary>
public class RunnerStatusResponse
{
    /// <summary>
    /// Indicates if there are any online runners
    /// </summary>
    public bool HasOnlineRunners { get; set; }

    /// <summary>
    /// Total number of registered runners
    /// </summary>
    public int TotalRunners { get; set; }

    /// <summary>
    /// Number of currently online runners
    /// </summary>
    public int OnlineRunners { get; set; }

    /// <summary>
    /// Number of offline runners
    /// </summary>
    public int OfflineRunners => TotalRunners - OnlineRunners;
}