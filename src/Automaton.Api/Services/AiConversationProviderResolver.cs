using Automaton.Api.Config;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

public sealed class AiConversationProviderResolver(
    AiApiKeyService apiKeyService,
    IAiProvidersSettingsService settingsService,
    AiProviderMetadataService providerMetadataService,
    ITokenBudgetService tokenBudgetService,
    ILogger<AiConversationProviderResolver> logger)
{
    public async Task<AiConversationProviderResolution> ResolveAsync(
        Guid userId,
        string provider,
        string? requestedModel)
    {
        var model = requestedModel;
        if (string.IsNullOrWhiteSpace(model))
            model = await providerMetadataService.GetPreferredModelAsync(provider);

        if (string.IsNullOrWhiteSpace(model))
        {
            return AiConversationProviderResolution.Failed(
                $"No active model configured for provider '{provider}'.");
        }

        model = await tokenBudgetService.GetEffectiveModelAsync(userId, provider, model);
        var settings = await settingsService.GetEffectiveConfigAsync();
        var maxTokens = await providerMetadataService.GetModelMaxTokensAsync(provider, model);
        var extraBodyJson = await providerMetadataService.GetModelExtraBodyJsonAsync(provider, model);
        var supportsTools = await providerMetadataService.GetModelSupportsToolsAsync(provider, model);
        var (apiKey, isSystemKey) = await apiKeyService.GetDecryptedApiKeyWithSourceAsync(userId, provider);

        if (string.IsNullOrEmpty(apiKey))
        {
            return AiConversationProviderResolution.Failed(
                $"No API key configured for provider '{provider}'.");
        }

        logger.LogDebug(
            "Starting AI conversation for user {UserId}, provider {Provider}, model {Model}, systemKey={IsSystemKey}",
            userId,
            provider,
            model,
            isSystemKey);

        return AiConversationProviderResolution.Succeeded(new AiConversationProviderConfiguration(
            provider,
            model,
            apiKey,
            isSystemKey,
            settings,
            maxTokens,
            extraBodyJson,
            supportsTools));
    }
}

public sealed record AiConversationProviderConfiguration(
    string Provider,
    string Model,
    string ApiKey,
    bool IsSystemKey,
    AiProvidersConfig Settings,
    int? MaxTokens,
    string? ExtraBodyJson,
    bool SupportsTools);

public sealed record AiConversationProviderResolution(
    AiConversationProviderConfiguration? Configuration,
    string? ErrorMessage)
{
    public static AiConversationProviderResolution Failed(string errorMessage) => new(null, errorMessage);

    public static AiConversationProviderResolution Succeeded(AiConversationProviderConfiguration configuration) =>
        new(configuration, null);
}
