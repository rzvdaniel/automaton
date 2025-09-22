using System.ComponentModel.DataAnnotations;

namespace Automaton.Studio.Models;

/// <summary>
/// Model for AI service configuration
/// </summary>
public class AiConfiguration
{
    /// <summary>
    /// OpenAI API Key
    /// </summary>
    [Display(Name = "OpenAI API Key")]
    public string? OpenAiApiKey { get; set; }

    /// <summary>
    /// OpenAI Model to use (optional, defaults to gpt-4)
    /// </summary>
    [Display(Name = "OpenAI Model")]
    public string? OpenAiModel { get; set; } = "gpt-4";

    /// <summary>
    /// Whether OpenAI is enabled
    /// </summary>
    public bool IsOpenAiEnabled => !string.IsNullOrWhiteSpace(OpenAiApiKey);
}

/// <summary>
/// Request model for updating AI configuration
/// </summary>
public class UpdateAiConfigurationRequest
{
    /// <summary>
    /// OpenAI API Key
    /// </summary>
    public string? OpenAiApiKey { get; set; }

    /// <summary>
    /// OpenAI Model to use
    /// </summary>
    public string? OpenAiModel { get; set; }
}

/// <summary>
/// Response model for AI configuration
/// </summary>
public class AiConfigurationResponse
{
    /// <summary>
    /// Whether OpenAI is configured (doesn't expose actual key)
    /// </summary>
    public bool IsOpenAiConfigured { get; set; }

    /// <summary>
    /// OpenAI Model being used
    /// </summary>
    public string? OpenAiModel { get; set; }

    /// <summary>
    /// Masked OpenAI API Key (shows only first and last few characters)
    /// </summary>
    public string? OpenAiApiKeyMasked { get; set; }
}