using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Server.Models;

/// <summary>
/// Response model for runner status endpoint
/// </summary>
public class RunnerStatusResponse
{
    /// <summary>
    /// Indicates if there are any online runners
    /// </summary>
    [Required]
    public bool HasOnlineRunners { get; set; }

    /// <summary>
    /// Total number of registered runners
    /// </summary>
    [Required]
    public int TotalRunners { get; set; }

    /// <summary>
    /// Number of currently online runners
    /// </summary>
    [Required]
    public int OnlineRunners { get; set; }

    /// <summary>
    /// Number of offline runners
    /// </summary>
    [Required]
    public int OfflineRunners => TotalRunners - OnlineRunners;
}