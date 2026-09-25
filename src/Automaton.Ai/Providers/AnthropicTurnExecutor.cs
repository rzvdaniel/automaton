using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Tool = Anthropic.SDK.Common.Tool;

namespace Automaton.Ai.Providers;

internal sealed class AnthropicTurnExecutor
{
    private const int DefaultMaxTokens = 8192;
    private static readonly System.Reflection.PropertyInfo? HttpClientProperty =
        typeof(AnthropicClient).GetProperty(
            "HttpClient",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    private static readonly HttpClient StreamingHttpClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };
    private readonly ILogger _logger;
    private readonly AnthropicStreamResponseProcessor _responseProcessor;
    private readonly AnthropicConversationEventEmitter _events;

    public AnthropicTurnExecutor(
        ILogger logger,
        AnthropicStreamResponseProcessor responseProcessor,
        AnthropicConversationEventEmitter events)
    {
        _logger = logger;
        _responseProcessor = responseProcessor;
        _events = events;
    }

    public AnthropicClient CreateClient(string apiKey)
    {
        var client = new AnthropicClient(apiKey);
        HttpClientProperty?.SetValue(client, StreamingHttpClient);
        return client;
    }

    public Task<AnthropicStreamResponseProcessor.StreamResult> ExecuteAsync(
        AnthropicClient client,
        ConversationRequest request,
        List<Message> messages,
        List<Tool> tools,
        bool suppressTools,
        TokenUsageAccumulator tokenUsage,
        CancellationToken cancellationToken)
    {
        var parameters = CreateMessageParameters(request, messages, tools, suppressTools);
        var retryPipeline = CreateRetryPipeline(request);
        return retryPipeline.ExecuteAsync(
            async ct => await _responseProcessor.ProcessAsync(
                client,
                parameters,
                request.ConversationId,
                ct,
                accumulatedInputTokens: tokenUsage.InputTokens,
                accumulatedOutputTokens: tokenUsage.OutputTokens,
                accumulatedCacheReadTokens: tokenUsage.CacheReadTokens,
                accumulatedCacheCreationTokens: tokenUsage.CacheCreationTokens),
            cancellationToken).AsTask();
    }

    private static MessageParameters CreateMessageParameters(
        ConversationRequest request,
        List<Message> messages,
        List<Tool> tools,
        bool suppressTools) =>
        new()
        {
            Model = request.Model ?? Anthropic.SDK.Constants.AnthropicModels.Claude35Sonnet,
            MaxTokens = request.MaxTokens ?? DefaultMaxTokens,
            System = BuildSystemMessages(request),
            Messages = messages,
            Tools = (!suppressTools && tools.Count > 0) ? tools : null,
            Stream = true
        };

    private static List<SystemMessage>? BuildSystemMessages(ConversationRequest request)
    {
        if (request.SystemPromptParts is { } parts)
        {
            var messages = new List<SystemMessage>
            {
                new SystemMessage(parts.BasePrompt)
                {
                    CacheControl = new CacheControl { Type = CacheControlType.ephemeral }
                }
            };
            AddSystemPromptPart(messages, parts.InstructionBlock);
            AddSystemPromptPart(messages, parts.MemoryBlock);
            return messages;
        }

        if (string.IsNullOrEmpty(request.SystemPrompt))
            return null;

        return new List<SystemMessage>
        {
            new SystemMessage(request.SystemPrompt)
            {
                CacheControl = new CacheControl { Type = CacheControlType.ephemeral }
            }
        };
    }

    private static void AddSystemPromptPart(List<SystemMessage> messages, string? prompt)
    {
        if (!string.IsNullOrWhiteSpace(prompt))
            messages.Add(new SystemMessage(prompt));
    }

    private ResiliencePipeline<AnthropicStreamResponseProcessor.StreamResult> CreateRetryPipeline(
        ConversationRequest request)
    {
        return new ResiliencePipelineBuilder<AnthropicStreamResponseProcessor.StreamResult>()
            .AddRetry(new RetryStrategyOptions<AnthropicStreamResponseProcessor.StreamResult>
            {
                MaxRetryAttempts = request.MaxRateLimitRetries,
                Delay = TimeSpan.FromSeconds(request.RateLimitRetryInitialDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<AnthropicStreamResponseProcessor.StreamResult>()
                    .Handle<Exception>(IsRateLimitException),
                DelayGenerator = static args =>
                {
                    if (TryParseRetryAfter(args.Outcome.Exception, out var retryAfter))
                        return ValueTask.FromResult<TimeSpan?>(retryAfter);
                    return ValueTask.FromResult<TimeSpan?>(null);
                },
                OnRetry = args =>
                {
                    var delaySecs = (int)args.RetryDelay.TotalSeconds;
                    _logger.LogWarning(
                        "Anthropic rate limit hit (attempt {Retry}/{MaxRetries}). Model={Model}, Retrying in {Delay}s. Reason: {Error}",
                        args.AttemptNumber + 1, request.MaxRateLimitRetries,
                        request.Model, delaySecs, args.Outcome.Exception?.Message);
                    _events.EmitRetryNotification(request.ConversationId, args.AttemptNumber + 1,
                        request.MaxRateLimitRetries, delaySecs);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private static bool IsRateLimitException(Exception exception)
    {
        var message = exception.Message;
        if (ContainsQuotaFailure(message))
            return false;

        return message.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("rate limited", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("429", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsQuotaFailure(string message) =>
        message.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("quota_exceeded", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("exceeded_current_quota", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("suspended", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseRetryAfter(Exception? exception, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (exception is null)
            return false;

        var match = RetryAfterPattern.Match(exception.Message);
        if (!match.Success || !int.TryParse(match.Groups["secs"].Value, out var seconds) || seconds <= 0)
            return false;

        retryAfter = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static readonly System.Text.RegularExpressions.Regex RetryAfterPattern = new(
        @"retry[- ](?:after|in)\D*(?<secs>\d+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.Compiled);
}
