using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service to seed AI provider and model configuration from appsettings.json into database
/// This runs once on application startup if the database is empty
/// </summary>
public class AiProviderSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly AiProvidersConfig _aiProvidersConfig;
    private readonly ILogger<AiProviderSeeder> _logger;

    public AiProviderSeeder(
        ApplicationDbContext dbContext,
        IOptions<AiProvidersConfig> aiProvidersConfig,
        ILogger<AiProviderSeeder> logger)
    {
        _dbContext = dbContext;
        _aiProvidersConfig = aiProvidersConfig.Value;
        _logger = logger;
    }

    /// <summary>
    /// Seeds AI providers from configuration if database is empty
    /// </summary>
    public async Task SeedProvidersAsync()
    {
        try
        {
            // Check if providers already exist
            var existingProviders = await _dbContext.AiProviderConfigs.AnyAsync();
            if (existingProviders)
            {
                _logger.LogInformation("AI providers already seeded, skipping seed operation");
                return;
            }

            _logger.LogInformation("Seeding AI providers from configuration...");

            // Use system user ID (all zeros) for seeding
            var systemUserId = Guid.Empty;
            var now = DateTime.UtcNow;
            var displayOrder = 0;

            foreach (var providerConfig in _aiProvidersConfig.Providers)
            {
                var providerName = providerConfig.Key;
                var config = providerConfig.Value;

                // Map provider name to enum
                if (!Enum.TryParse<AiProvider>(providerName, out var providerEnum))
                {
                    _logger.LogWarning("Unknown provider '{ProviderName}', skipping", providerName);
                    continue;
                }

                // Create provider (use fully qualified name to avoid ambiguity with Config.AiProviderConfig)
                var provider = new Entities.AiProviderConfig
                {
                    Id = Guid.NewGuid(),
                    ProviderName = providerName,
                    Provider = providerEnum,
                    DefaultModel = config.DefaultModel,
                    Description = GetProviderDescription(providerEnum),
                    Enabled = config.Enabled,
                    DisplayOrder = displayOrder++,
                    CreatedAt = now,
                    CreatedBy = systemUserId
                };

                _dbContext.AiProviderConfigs.Add(provider);

                // Create models
                var modelDisplayOrder = 0;
                foreach (var modelName in config.SupportedModels)
                {
                    var model = new AiModelConfig
                    {
                        Id = Guid.NewGuid(),
                        ProviderId = provider.Id,
                        ModelName = modelName,
                        DisplayName = GetModelDisplayName(providerEnum, modelName),
                        Description = GetModelDescription(providerEnum, modelName),
                        Enabled = true,
                        DisplayOrder = modelDisplayOrder++,
                        CreatedAt = now,
                        CreatedBy = systemUserId
                    };

                    _dbContext.AiModelConfigs.Add(model);
                }

                _logger.LogInformation("Seeded provider '{ProviderName}' with {ModelCount} models", 
                    providerName, config.SupportedModels.Count);
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("AI provider seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding AI providers from configuration");
            throw;
        }
    }

    private static string GetProviderDescription(AiProvider provider) => provider switch
    {
        AiProvider.OpenAI => "OpenAI's GPT models including GPT-4o, o1, and legacy versions",
        AiProvider.Anthropic => "Anthropic's Claude 3.5 family including Sonnet, Haiku, and Opus",
        AiProvider.Google => "Google's Gemini 1.5 models including Pro and Flash variants",
        _ => $"{provider} AI models"
    };

    private static string GetModelDisplayName(AiProvider provider, string modelName) => (provider, modelName) switch
    {
        // OpenAI
        (AiProvider.OpenAI, "o1-preview") => "o1 Preview",
        (AiProvider.OpenAI, "o1-mini") => "o1 Mini",
        (AiProvider.OpenAI, "gpt-4o") => "GPT-4o",
        (AiProvider.OpenAI, "gpt-4o-mini") => "GPT-4o Mini",
        (AiProvider.OpenAI, "gpt-4-turbo") => "GPT-4 Turbo",
        (AiProvider.OpenAI, "gpt-4") => "GPT-4",
        (AiProvider.OpenAI, "gpt-3.5-turbo") => "GPT-3.5 Turbo",

        // Anthropic
        (AiProvider.Anthropic, "claude-3-5-sonnet-20241022") => "Claude 3.5 Sonnet (Oct 2024)",
        (AiProvider.Anthropic, "claude-3-5-haiku-20241022") => "Claude 3.5 Haiku (Oct 2024)",
        (AiProvider.Anthropic, "claude-3-opus-20240229") => "Claude 3 Opus",
        (AiProvider.Anthropic, "claude-3-sonnet-20240229") => "Claude 3 Sonnet",
        (AiProvider.Anthropic, "claude-3-haiku-20240307") => "Claude 3 Haiku",

        // Google
        (AiProvider.Google, "gemini-1.5-pro-002") => "Gemini 1.5 Pro (002)",
        (AiProvider.Google, "gemini-1.5-flash-002") => "Gemini 1.5 Flash (002)",
        (AiProvider.Google, "gemini-1.5-pro") => "Gemini 1.5 Pro",
        (AiProvider.Google, "gemini-1.5-flash") => "Gemini 1.5 Flash",
        (AiProvider.Google, "gemini-1.0-pro") => "Gemini 1.0 Pro",
        (AiProvider.Google, "gemini-pro") => "Gemini Pro (Legacy)",

        // Default
        _ => modelName
    };

    private static string GetModelDescription(AiProvider provider, string modelName) => (provider, modelName) switch
    {
        // OpenAI
        (AiProvider.OpenAI, "o1-preview") => "Advanced reasoning model with extended thinking capabilities",
        (AiProvider.OpenAI, "o1-mini") => "Faster reasoning model optimized for STEM tasks",
        (AiProvider.OpenAI, "gpt-4o") => "Most capable model, multimodal, 128K context",
        (AiProvider.OpenAI, "gpt-4o-mini") => "Fast and cost-effective, 128K context",
        (AiProvider.OpenAI, "gpt-4-turbo") => "Previous generation flagship, 128K context",
        (AiProvider.OpenAI, "gpt-4") => "Original GPT-4, 8K context",
        (AiProvider.OpenAI, "gpt-3.5-turbo") => "Legacy model, fast and affordable, 16K context",

        // Anthropic
        (AiProvider.Anthropic, "claude-3-5-sonnet-20241022") => "Most capable Claude model, excellent for complex tasks, 200K context",
        (AiProvider.Anthropic, "claude-3-5-haiku-20241022") => "Fastest Claude model, cost-effective, 200K context",
        (AiProvider.Anthropic, "claude-3-opus-20240229") => "Previous flagship model, 200K context",
        (AiProvider.Anthropic, "claude-3-sonnet-20240229") => "Balanced performance and speed, 200K context",
        (AiProvider.Anthropic, "claude-3-haiku-20240307") => "Fast and affordable, 200K context",

        // Google
        (AiProvider.Google, "gemini-1.5-pro-002") => "Latest Gemini Pro with improved capabilities, 2M context",
        (AiProvider.Google, "gemini-1.5-flash-002") => "Latest Flash model, fast and efficient, 1M context",
        (AiProvider.Google, "gemini-1.5-pro") => "Stable Gemini Pro identifier, 2M context",
        (AiProvider.Google, "gemini-1.5-flash") => "Stable Flash identifier, 1M context",
        (AiProvider.Google, "gemini-1.0-pro") => "Previous generation Gemini, 32K context",
        (AiProvider.Google, "gemini-pro") => "Legacy Gemini Pro identifier",

        // Default
        _ => null
    };
}
