using Automaton.Mcp.Client;
using Automaton.Mcp.Client.Providers;
using Automaton.Mcp.Models;
using Automaton.Studio.Server.Config;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// System-level AI service implementation that uses Automaton's API key
/// to provide AI capabilities to all users within their quotas.
/// Uses the existing AI provider infrastructure for flexibility.
/// </summary>
public class SystemAiService : ISystemAiService
{
    private readonly SystemAiConfig _config;
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly ILogger<SystemAiService> _logger;
    private readonly IMemoryCache _cache;

    // Rate limiting cache key prefix
    private const string RateLimitKeyPrefix = "SystemAi:RateLimit:";

    public SystemAiService(
        IOptions<SystemAiConfig> config,
        IAiProviderRegistry providerRegistry,
        ILogger<SystemAiService> logger,
        IMemoryCache cache)
    {
        _config = config.Value;
        _providerRegistry = providerRegistry;
        _logger = logger;
        _cache = cache;
    }

    /// <inheritdoc />
    public bool IsAvailable => _config.Enabled && !string.IsNullOrEmpty(_config.ApiKey);

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
    public async Task<string> CompleteAsync(string prompt, string? systemPrompt = null, string? userId = null)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("System AI service is not configured or disabled.");
        }

        // Check rate limit
        if (!string.IsNullOrEmpty(userId))
        {
            if (!CheckRateLimit(userId))
            {
                throw new InvalidOperationException("Rate limit exceeded. Please try again later.");
            }
        }

        // Get the provider
        var provider = _providerRegistry.GetProvider(_config.DefaultProvider);
        if (provider == null)
        {
            throw new InvalidOperationException($"AI provider '{_config.DefaultProvider}' is not available.");
        }

        try
        {
            _logger.LogInformation(
                "System AI request: Provider={Provider}, Model={Model}, UserId={UserId}",
                _config.DefaultProvider, _config.DefaultModel, userId ?? "anonymous");

            var request = new ConversationRequest
            {
                Provider = _config.DefaultProvider,
                Model = _config.DefaultModel,
                ApiKey = _config.ApiKey,
                InitialMessage = prompt,
                SystemPrompt = systemPrompt ?? string.Empty,
                MaxIterations = 1, // No tool calls for simple completion
                MaxConsecutiveFailures = 1
            };

            // Call with empty tools for simple completion
            var result = await provider.CallAsync(
                request,
                Enumerable.Empty<ProviderTool>(),
                (_, _, _) => Task.FromResult(string.Empty), // No-op tool executor
                CancellationToken.None);

            _logger.LogInformation(
                "System AI response completed: UserId={UserId}, ResponseLength={Length}",
                userId ?? "anonymous", result.Length);

            // TODO: Store token usage in database for quota tracking
            // Token counts are logged by the provider, we can capture them via events if needed

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System AI request failed: UserId={UserId}", userId ?? "anonymous");
            throw;
        }
    }

    /// <summary>
    /// Check and update rate limit for a user.
    /// Uses sliding window rate limiting.
    /// </summary>
    private bool CheckRateLimit(string userId)
    {
        var cacheKey = $"{RateLimitKeyPrefix}{userId}";
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-1);

        // Get or create request timestamps list
        var timestamps = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return new List<DateTime>();
        })!;

        // Remove expired timestamps
        timestamps.RemoveAll(t => t < windowStart);

        // Check if under limit
        if (timestamps.Count >= _config.MaxRequestsPerUserPerMinute)
        {
            _logger.LogWarning(
                "Rate limit exceeded for user {UserId}: {RequestCount} requests in last minute",
                userId, timestamps.Count);
            return false;
        }

        // Add current request timestamp
        timestamps.Add(now);
        _cache.Set(cacheKey, timestamps, TimeSpan.FromMinutes(2));

        return true;
    }
}
