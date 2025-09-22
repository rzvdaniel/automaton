using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Studio.Domain;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services.AI;

/// <summary>
/// Main service for AI-powered custom step generation
/// Coordinates multiple AI providers and handles step creation
/// </summary>
public class AiStepGenerationService
{
    private readonly IEnumerable<IAiStepGenerationProvider> providers;
    private readonly ILogger<AiStepGenerationService> logger;

    public AiStepGenerationService(
        IEnumerable<IAiStepGenerationProvider> providers,
        ILogger<AiStepGenerationService> logger)
    {
        this.providers = providers;
        this.logger = logger;
    }

    /// <summary>
    /// Get all available AI providers
    /// </summary>
    public async Task<List<string>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default)
    {
        var availableProviders = new List<string>();
        
        foreach (var provider in providers)
        {
            try
            {
                if (await provider.IsAvailableAsync(cancellationToken))
                {
                    availableProviders.Add(provider.ProviderName);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to check availability of AI provider {ProviderName}", provider.ProviderName);
            }
        }

        return availableProviders;
    }

    /// <summary>
    /// Generate custom step definition using AI (without saving to database)
    /// </summary>
    public async Task<AiGeneratedStepResult> GenerateStepDefinitionAsync(
        string description,
        string language = "Python",
        string? preferredProvider = null,
        IProgress<AiGenerationProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting AI custom step generation: {Description} in {Language}", description, language);

        // Find the provider to use
        var provider = await GetProviderToUseAsync(preferredProvider, cancellationToken);
        if (provider == null)
        {
            return new AiGeneratedStepResult
            {
                IsSuccess = false,
                ErrorMessage = "No AI providers are available. Please configure at least one AI provider (e.g., OpenAI API key)."
            };
        }

        // Create the generation request
        var request = new AiStepGenerationRequest
        {
            Description = description,
            Language = language,
            Context = $"Generate a custom step for the Automaton workflow automation platform."
        };

        // Generate the step using AI
        var result = await provider.GenerateCustomStepAsync(request, progressCallback, cancellationToken);
        
        if (result.IsSuccess)
        {
            logger.LogInformation("Successfully generated AI custom step definition using provider {ProviderName}", provider.ProviderName);
        }

        return result;
    }

    /// <summary>
    /// Apply AI-generated result to a CustomStep definition
    /// </summary>
    public void ApplyGeneratedResultToCustomStep(CustomStep customStep, AiGeneratedStepResult aiResult)
    {
        if (!aiResult.IsSuccess || customStep?.Definition == null)
        {
            return;
        }

        // Update basic step properties
        customStep.DisplayName = aiResult.DisplayName ?? customStep.Name;
        customStep.Description = aiResult.Description ?? "AI-generated custom step";
        
        // Update the step definition
        customStep.Definition.Language = aiResult.Language;
        customStep.Definition.Code = FormatCodeFromAi(aiResult.Code ?? "");
        
        // Convert AI variables to step variables
        customStep.Definition.CodeInputVariables = aiResult.InputVariables.Select(ConvertToStepVariable).ToList();
        customStep.Definition.CodeOutputVariables = aiResult.OutputVariables.Select(ConvertToStepVariable).ToList();
    }

    /// <summary>
    /// Format AI-generated code by converting escaped characters to actual characters
    /// </summary>
    /// <param name="aiCode">The raw AI-generated code with escaped characters</param>
    /// <returns>Properly formatted code with actual line breaks and characters</returns>
    public static string FormatCodeFromAi(string aiCode)
    {
        if (string.IsNullOrEmpty(aiCode))
        {
            return string.Empty;
        }

        // Handle different variations of escaped characters that AI might generate
        var formattedCode = aiCode
            // First handle Windows line endings (must come before single \n)
            .Replace("\\r\\n", "\n")
            .Replace("\\n\\r", "\n")
            // Then handle individual line endings
            .Replace("\\n", "\n")
            .Replace("\\r", "\n")
            // Handle tabs
            .Replace("\\t", "\t")
            // Handle quotes (single and double)
            .Replace("\\\"", "\"")
            .Replace("\\'", "'")
            // Handle backslashes (must be last to avoid double processing)
            .Replace("\\\\", "\\");

        // Clean up any potential double newlines that might result from the conversion
        while (formattedCode.Contains("\n\n\n"))
        {
            formattedCode = formattedCode.Replace("\n\n\n", "\n\n");
        }

        return formattedCode;
    }

    private async Task<IAiStepGenerationProvider?> GetProviderToUseAsync(string? preferredProvider, CancellationToken cancellationToken)
    {
        // If a specific provider is requested, try to use it
        if (!string.IsNullOrEmpty(preferredProvider))
        {
            var specificProvider = providers.FirstOrDefault(p => 
                p.ProviderName.Equals(preferredProvider, StringComparison.OrdinalIgnoreCase));
            
            if (specificProvider != null && await specificProvider.IsAvailableAsync(cancellationToken))
            {
                return specificProvider;
            }
        }

        // Otherwise, find the first available provider
        foreach (var provider in providers)
        {
            try
            {
                if (await provider.IsAvailableAsync(cancellationToken))
                {
                    return provider;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Provider {ProviderName} is not available", provider.ProviderName);
            }
        }

        return null;
    }

    private static StepVariable ConvertToStepVariable(AiGeneratedVariable aiVariable)
    {
        return new StepVariable
        {
            Id = Guid.NewGuid().ToString(),
            Name = aiVariable.Name,
            Type = ParseVariableType(aiVariable.Type),
            Description = aiVariable.Description ?? "",
            Value = aiVariable.DefaultValue ?? ""
        };
    }

    private static VariableType ParseVariableType(string type)
    {
        return type?.ToLower() switch
        {
            "string" => VariableType.String,
            "number" => VariableType.Number,
            "boolean" => VariableType.Boolean,
            "text" => VariableType.Text,
            "date" => VariableType.Date,
            _ => VariableType.String // Default to string
        };
    }
}