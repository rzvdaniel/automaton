using Automaton.Studio.Models;

namespace Automaton.Studio.Services;

/// <summary>
/// Interface for AI configuration management
/// </summary>
public interface IAiConfigurationService
{
    /// <summary>
    /// Get current AI configuration
    /// </summary>
    Task<AiConfigurationResponse> GetConfigurationAsync();

    /// <summary>
    /// Update AI configuration
    /// </summary>
    Task<bool> UpdateConfigurationAsync(UpdateAiConfigurationRequest request);

    /// <summary>
    /// Test AI provider availability
    /// </summary>
    Task<Dictionary<string, bool>> TestProvidersAsync();
}