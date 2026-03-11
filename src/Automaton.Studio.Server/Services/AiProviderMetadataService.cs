using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

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
