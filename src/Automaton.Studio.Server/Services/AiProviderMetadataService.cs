using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for retrieving AI provider and model metadata from the database
/// </summary>
public class AiProviderMetadataService
{
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<AiProviderMetadataService> logger;

    public AiProviderMetadataService(ApplicationDbContext dbContext, ILogger<AiProviderMetadataService> logger)
    {
        this.dbContext = dbContext;
        this.logger = logger;
    }

    /// <summary>
    /// Get all supported models for a provider
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSupportedModelsAsync(AiProviderType providerType)
    {
        var providerName = providerType.ToString();

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
    public async Task<string?> GetDefaultModelAsync(AiProviderType providerType)
    {
        var providerName = providerType.ToString();

        var defaultModel = await dbContext.AiModels
            .Where(m => m.Provider.Name == providerName && m.IsActive && m.IsDefault)
            .Select(m => m.ModelId)
            .FirstOrDefaultAsync();

        return defaultModel;
    }

    /// <summary>
    /// Get fallback default model for a provider (used when no default is set in database)
    /// </summary>
    public string GetFallbackDefaultModel(AiProviderType providerType)
    {
        return providerType switch
        {
            AiProviderType.OpenAI => "gpt-4-turbo",
            AiProviderType.Anthropic => "claude-3-5-sonnet-20241022",
            AiProviderType.Google => "gemini-1.5-pro",
            _ => "unknown-model"
        };
    }

    /// <summary>
    /// Get default model with fallback
    /// </summary>
    public async Task<string> GetDefaultModelWithFallbackAsync(AiProviderType providerType)
    {
        var defaultModel = await GetDefaultModelAsync(providerType);
        return defaultModel ?? GetFallbackDefaultModel(providerType);
    }

    /// <summary>
    /// Check if a provider is active in the database
    /// </summary>
    public async Task<bool> IsProviderActiveAsync(AiProviderType providerType)
    {
        var providerName = providerType.ToString();

        return await dbContext.AiProviders
            .Where(p => p.Name == providerName && p.IsActive)
            .AnyAsync();
    }

    /// <summary>
    /// Get all active provider types
    /// </summary>
    public async Task<IReadOnlyList<AiProviderType>> GetActiveProviderTypesAsync()
    {
        var providerNames = await dbContext.AiProviders
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .Select(p => p.Name)
            .ToListAsync();

        var providerTypes = new List<AiProviderType>();

        foreach (var name in providerNames)
        {
            if (Enum.TryParse<AiProviderType>(name, true, out var providerType))
            {
                providerTypes.Add(providerType);
            }
            else
            {
                logger.LogWarning("Unknown provider type in database: {ProviderName}", name);
            }
        }

        return providerTypes.AsReadOnly();
    }
}
