using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Service for retrieving AI provider and model metadata from the database
/// </summary>
public class AiProviderMetadataService
{
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<AiProviderMetadataService> logger;

    // Known fallback defaults when database has no default set
    private static readonly Dictionary<string, string> FallbackDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OpenAI"] = "gpt-4-turbo",
        ["Anthropic"] = "claude-3-5-sonnet-20241022",
        ["Google"] = "gemini-1.5-pro"
    };

    public AiProviderMetadataService(ApplicationDbContext dbContext, ILogger<AiProviderMetadataService> logger)
    {
        this.dbContext = dbContext;
        this.logger = logger;
    }

    /// <summary>
    /// Get all supported models for a provider
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSupportedModelsAsync(string providerName)
    {
        var models = await dbContext.AiModels
            .Where(m => m.Provider.Name == providerName && m.IsActive)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.ModelId)
            .ToListAsync();

        return models.AsReadOnly();
    }

    /// <summary>
    /// Get the default model for a provider
    /// </summary>
    public async Task<string?> GetDefaultModelAsync(string providerName)
    {
        var defaultModel = await dbContext.AiModels
            .Where(m => m.Provider.Name == providerName && m.IsActive && m.IsDefault)
            .Select(m => m.ModelId)
            .FirstOrDefaultAsync();

        return defaultModel;
    }

    /// <summary>
    /// Get fallback default model for a provider (used when no default is set in database)
    /// </summary>
    public string GetFallbackDefaultModel(string providerName)
    {
        if (FallbackDefaults.TryGetValue(providerName, out var fallback))
        {
            return fallback;
        }

        logger.LogWarning("No fallback default model defined for provider: {ProviderName}", providerName);
        return "unknown-model";
    }

    /// <summary>
    /// Get default model with fallback
    /// </summary>
    public async Task<string> GetDefaultModelWithFallbackAsync(string providerName)
    {
        var defaultModel = await GetDefaultModelAsync(providerName);
        return defaultModel ?? GetFallbackDefaultModel(providerName);
    }

    /// <summary>
    /// Get the globally-designated default model (across all providers).
    /// Returns null if no model has IsDefault=true anywhere.
    /// </summary>
    public async Task<(string ProviderName, string ModelId)?> GetGlobalDefaultModelAsync()
    {
        var result = await dbContext.AiModels
            .Where(m => m.IsActive && m.IsDefault)
            .Select(m => new { ProviderName = m.Provider.Name, m.ModelId })
            .FirstOrDefaultAsync();

        return result != null ? (result.ProviderName, result.ModelId) : null;
    }

    /// <summary>
    /// Get the globally-designated planning model (across all providers).
    /// Returns null if no model has IsPlanningModel=true anywhere.
    /// </summary>
    public async Task<(string ProviderName, string ModelId)?> GetGlobalPlanningModelAsync()
    {
        var result = await dbContext.AiModels
            .Where(m => m.IsActive && m.IsPlanningModel)
            .Select(m => new { ProviderName = m.Provider.Name, m.ModelId })
            .FirstOrDefaultAsync();

        return result != null ? (result.ProviderName, result.ModelId) : null;
    }

    /// <summary>
    /// Get the preferred model for a provider (default if set, otherwise first by sort order).
    /// </summary>
    public async Task<string?> GetPreferredModelAsync(string providerName)
    {
        return await dbContext.AiModels
            .Where(m => m.Provider.Name == providerName && m.IsActive)
            .OrderByDescending(m => m.IsDefault)
            .ThenBy(m => m.SortOrder)
            .Select(m => m.ModelId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Get the designated planning model for a provider — the model to use during the plan phase.
    /// Returns null if no model has IsPlanningModel=true for this provider.
    /// </summary>
    public async Task<string?> GetPlanningModelAsync(string providerName)
    {
        return await dbContext.AiModels
            .Where(m => m.Provider.Name == providerName && m.IsActive && m.IsPlanningModel)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.ModelId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Check if a provider is active in the database
    /// </summary>
    public async Task<bool> IsProviderActiveAsync(string providerName)
    {
        return await dbContext.AiProviders
            .Where(p => p.Name == providerName && p.IsActive)
            .AnyAsync();
    }

    /// <summary>
    /// Returns the MaxTokens configured for the specified model, or <c>null</c> if none is set.
    /// A <c>null</c> return means "let the provider decide" — callers should omit the max-tokens
    /// field from the API request (except Anthropic, which requires it).
    /// </summary>
    public async Task<int?> GetModelMaxTokensAsync(string providerName, string modelId)
    {
        return await dbContext.AiModels
            .Where(m => m.Provider.Name == providerName && m.ModelId == modelId && m.IsActive)
            .Select(m => m.MaxTokens)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Returns the ExtraBodyJson configured for the specified model, or null if none is set.
    /// Used to inject provider-specific request body parameters (e.g. {"reasoning":{"enabled":true}} for OpenRouter).
    /// Provider-level ExtraBodyJson is used as the base; model-level values are merged on top (model wins on conflict).
    /// </summary>
    public async Task<string?> GetModelExtraBodyJsonAsync(string providerName, string modelId)
    {
        var row = await dbContext.AiModels
            .Where(m => m.Provider.Name == providerName && m.ModelId == modelId && m.IsActive)
            .Select(m => new { m.ExtraBodyJson, ProviderExtraBodyJson = m.Provider.ExtraBodyJson })
            .FirstOrDefaultAsync();

        if (row == null)
            return null;

        // If only one level is set, return it directly
        if (string.IsNullOrWhiteSpace(row.ProviderExtraBodyJson))
            return row.ExtraBodyJson;

        if (string.IsNullOrWhiteSpace(row.ExtraBodyJson))
            return row.ProviderExtraBodyJson;

        // Both are set — merge: provider fields are the base, model fields override
        try
        {
            using var providerDoc = System.Text.Json.JsonDocument.Parse(row.ProviderExtraBodyJson);
            using var modelDoc = System.Text.Json.JsonDocument.Parse(row.ExtraBodyJson);

            using var ms = new System.IO.MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                // Provider fields first (base)
                foreach (var prop in providerDoc.RootElement.EnumerateObject())
                    prop.WriteTo(writer);
                // Model fields on top (override duplicates)
                foreach (var prop in modelDoc.RootElement.EnumerateObject())
                    prop.WriteTo(writer);
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            // Malformed JSON — return model-level value as fallback
            return row.ExtraBodyJson ?? row.ProviderExtraBodyJson;
        }
    }

    /// <summary>
    /// Returns whether the specified model supports tool/function calling.
    /// Defaults to true when the model is not found so existing models are not broken.
    /// </summary>
    public async Task<bool> GetModelSupportsToolsAsync(string providerName, string modelId)
    {
        var supportsTools = await dbContext.AiModels
            .Where(m => m.Provider.Name == providerName && m.ModelId == modelId && m.IsActive)
            .Select(m => (bool?)m.SupportsTools)
            .FirstOrDefaultAsync();

        return supportsTools ?? true;
    }

    /// <summary>
    /// Get all active provider names
    /// </summary>
    public async Task<IReadOnlyList<string>> GetActiveProviderNamesAsync()
    {
        var providerNames = await dbContext.AiProviders
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .Select(p => p.Name)
            .ToListAsync();

        return providerNames.AsReadOnly();
    }
}
