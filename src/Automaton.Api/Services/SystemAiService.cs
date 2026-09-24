using Automaton.Ai;
using Automaton.Ai.Models;
using Automaton.Ai.Providers;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Services;

/// <summary>
/// System-level AI service implementation that uses Automaton's API key
/// to provide AI capabilities to all users within their quotas.
/// Uses the existing AI provider infrastructure for flexibility.
/// </summary>
public class SystemAiService : ISystemAiService
{
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly ISystemAiRateLimitService _rateLimitService;
    private readonly AiApiKeyService _aiApiKeyService;
    private readonly AiProviderMetadataService _providerMetadataService;
    private readonly ITokenBudgetService _tokenBudgetService;
    private readonly ILogger<SystemAiService> _logger;

    public SystemAiService(
        IAiProviderRegistry providerRegistry,
        ISystemAiRateLimitService rateLimitService,
        AiApiKeyService aiApiKeyService,
        AiProviderMetadataService providerMetadataService,
        ITokenBudgetService tokenBudgetService,
        ILogger<SystemAiService> logger)
    {
        _providerRegistry = providerRegistry;
        _rateLimitService = rateLimitService;
        _aiApiKeyService = aiApiKeyService;
        _providerMetadataService = providerMetadataService;
        _tokenBudgetService = tokenBudgetService;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => _aiApiKeyService.HasAnyActiveKeys();

    /// <inheritdoc />
    public async Task<string> GenerateAppNameAsync(string description, string? userId = null)
    {
        var prompt = $"""
            Generate a short, catchy app name (2-4 words) based on this description.
            Return ONLY the app name, nothing else. No quotes, no explanation.

            Description: {description}
            """;

        var systemPrompt = "You are a helpful assistant that generates concise, professional app names. Always respond with just the app name, nothing else.";

        var result = await CompleteAsync(prompt, systemPrompt, userId);

        // Clean up the result - remove quotes, trim whitespace
        return result.Trim().Trim('"', '\'');
    }

    /// <inheritdoc />
    public async Task<string> GenerateSessionNameAsync(string prompt, string? userId = null)
    {
        var aiPrompt = $"""
            Generate a concise, descriptive chat session name (3-5 words) based on this prompt.
            The name should capture the main topic or intent of the conversation.
            Return ONLY the session name, nothing else. No quotes, no explanation.

            Prompt: {prompt}
            """;

        var systemPrompt = "You are a helpful assistant that generates concise, descriptive session names for chat conversations. Always respond with just the session name, nothing else.";

        var result = await CompleteAsync(aiPrompt, systemPrompt, userId);

        // Clean up the result - remove quotes, trim whitespace
        return result.Trim().Trim('"', '\'');
    }

    /// <inheritdoc />
    public async Task<string> SummarizeGenerationAsync(string userPrompt, string aiResponse, string? userId = null)
    {
        var aiPrompt = $$"""
            Summarize what changes the AI made to a web app based on the user's request and the AI's response.

            User requested: "{{userPrompt}}"

            AI response: "{{aiResponse}}"

            Format your response as valid JSON with this exact structure:
            {
              "overview": "1-2 sentence summary of what was built or changed",
              "changes": [
                "specific change 1",
                "specific change 2"
              ]
            }

            Focus on WHAT was built/changed, not on the conversation.
            Be specific — list individual elements, styles, or behaviors that were added or modified.
            Return ONLY the JSON, no markdown code blocks, no explanation.
            """;

        var systemPrompt = "You are a technical assistant that summarizes web app code changes. Always respond with valid JSON only, no additional text.";

        var result = await CompleteAsync(aiPrompt, systemPrompt, userId);

        result = result.Trim();
        if (result.StartsWith("```json")) result = result[7..];
        if (result.StartsWith("```")) result = result[3..];
        if (result.EndsWith("```")) result = result[..^3];

        return result.Trim();
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string prompt, string? systemPrompt = null, string? userId = null)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("No active AI provider with API keys is configured.");

        if (!string.IsNullOrEmpty(userId) && !_rateLimitService.CheckAndRecordRequest(userId))
            throw new InvalidOperationException("Rate limit exceeded. Please try again later.");

        var resolvedUserId = TryParseUserId(userId);
        var (providerName, model, apiKey) = await ResolveProviderAsync(resolvedUserId);

        var provider = _providerRegistry.GetProvider(providerName);
        if (provider == null)
            throw new InvalidOperationException($"AI provider '{providerName}' is not available.");

        try
        {
            _logger.LogDebug(
                "System AI request: Provider={Provider}, Model={Model}, UserId={UserId}",
                providerName, model, userId ?? "anonymous");

            var request = new ConversationRequest
            {
                Provider = providerName,
                Model = model,
                ApiKey = apiKey,
                InitialMessage = prompt,
                SystemPrompt = systemPrompt ?? string.Empty,
                MaxIterations = 1,
                MaxConsecutiveFailures = 1
            };

            var completion = await CallProviderAsync(provider, request);

            // Record usage as telemetry for future analytics and operations.
            if ((completion.InputTokens > 0 || completion.OutputTokens > 0) && resolvedUserId.HasValue)
            {
                await _tokenBudgetService.RecordUsageAsync(
                    resolvedUserId.Value, providerName, model, completion.InputTokens, completion.OutputTokens);

                _logger.LogDebug(
                    "System AI usage recorded: UserId={UserId}, Input={Input}, Output={Output}",
                    userId, completion.InputTokens, completion.OutputTokens);
            }

            _logger.LogDebug(
                "System AI response completed: UserId={UserId}, ResponseLength={Length}",
                userId ?? "anonymous", completion.ResponseText.Length);

            return completion.ResponseText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System AI request failed: UserId={UserId}", userId ?? "anonymous");
            throw;
        }
    }

    private static async Task<(string ResponseText, int InputTokens, int OutputTokens)> CallProviderAsync(
        IAiProvider provider,
        ConversationRequest request)
    {
        var inputTokens = 0;
        var outputTokens = 0;
        EventHandler<ConversationUpdatedEvent> tokenCapture = (_, update) =>
        {
            foreach (var message in update.Messages)
            {
                if (message.InputTokens.HasValue)
                    inputTokens = message.InputTokens.Value;
                if (message.OutputTokens.HasValue)
                    outputTokens += message.OutputTokens.Value;
            }
        };

        provider.ConversationUpdated += tokenCapture;
        try
        {
            var result = await provider.CallAsync(
                request,
                Enumerable.Empty<ProviderTool>(),
                (_, _, _) => Task.FromResult(string.Empty),
                CancellationToken.None);
            return (result.ResponseText, inputTokens, outputTokens);
        }
        finally
        {
            provider.ConversationUpdated -= tokenCapture;
        }
    }

    private async Task<(string Provider, string Model, string ApiKey)> ResolveProviderAsync(Guid? userId)
    {
        var resolvedUserId = userId ?? Guid.Empty;

        // Prefer the globally-designated default model — it determines both provider and model.
        // If the user has no accessible key for that provider (e.g. it is a personal admin key),
        // fall through to the generic fallback so the user can still use a system/demo key.
        var globalDefault = await _providerMetadataService.GetGlobalDefaultModelAsync();
        if (globalDefault.HasValue)
        {
            var apiKey = await _aiApiKeyService.GetDecryptedApiKeyForProviderAsync(resolvedUserId, globalDefault.Value.ProviderName);
            if (!string.IsNullOrWhiteSpace(apiKey))
                return (globalDefault.Value.ProviderName, globalDefault.Value.ModelId, apiKey);

            _logger.LogDebug(
                "Global default provider '{Provider}' has no accessible API key for user {UserId}, falling back to first available provider.",
                globalDefault.Value.ProviderName, resolvedUserId);
        }

        // Fallback: first provider with an active API key, then its preferred model
        var providerName = await _aiApiKeyService.GetFirstProviderWithActiveKeyAsync();
        if (string.IsNullOrWhiteSpace(providerName))
            throw new InvalidOperationException("No active AI provider with API keys is configured.");

        var model = await _providerMetadataService.GetPreferredModelAsync(providerName);
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException($"No active AI models configured for provider '{providerName}'.");

        var fallbackKey = await _aiApiKeyService.GetDecryptedApiKeyForProviderAsync(resolvedUserId, providerName);
        if (string.IsNullOrWhiteSpace(fallbackKey))
            throw new InvalidOperationException($"No active API key found for provider '{providerName}'.");

        return (providerName, model, fallbackKey);
    }

    private static Guid? TryParseUserId(string? userId)
    {
        if (Guid.TryParse(userId, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
