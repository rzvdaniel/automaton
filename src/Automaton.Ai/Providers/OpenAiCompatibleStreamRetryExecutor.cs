using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using Polly;
using Polly.Retry;

namespace Automaton.Ai.Providers;

internal sealed class OpenAiCompatibleStreamRetryExecutor
{
    private readonly ILogger _logger;
    private readonly OpenAiCompatibleStreamReader _streamReader;
    private readonly Action<string, int, int, int> _emitRetryNotification;
    private readonly string _providerName;

    public OpenAiCompatibleStreamRetryExecutor(
        ILogger logger,
        OpenAiCompatibleStreamReader streamReader,
        string providerName,
        Action<string, int, int, int> emitRetryNotification)
    {
        _logger = logger;
        _streamReader = streamReader;
        _providerName = providerName;
        _emitRetryNotification = emitRetryNotification;
    }

    public async Task<OpenAiCompatibleStreamReader.StreamResult> ExecuteAsync(
        ConversationRequest request,
        ChatClient client,
        List<ChatMessage> messages,
        ChatCompletionOptions options,
        CancellationToken cancellationToken)
    {
        var result = new OpenAiCompatibleStreamReader.StreamResult(
            string.Empty, string.Empty, null,
            new List<OpenAiCompatibleStreamReader.ToolCallAccumulator>(), null, null, null, null);
        var pipeline = CreatePipeline(request);
        await pipeline.ExecuteAsync(async token =>
        {
            result = await _streamReader.StreamOnceAsync(
                client, messages, options, request.ConversationId, request.StreamingTimeoutMinutes, token);
        }, cancellationToken);
        return result;
    }

    private ResiliencePipeline CreatePipeline(ConversationRequest request) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = request.MaxRateLimitRetries,
                Delay = TimeSpan.FromSeconds(request.RateLimitRetryInitialDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(OpenAiCompatibleRateLimits.IsRateLimitException),
                DelayGenerator = static args =>
                    OpenAiCompatibleRateLimits.TryParseRetryAfter(args.Outcome.Exception, out var retryAfter)
                        ? ValueTask.FromResult<TimeSpan?>(retryAfter)
                        : ValueTask.FromResult<TimeSpan?>(null),
                OnRetry = args => OnRetryAsync(request, args)
            })
            .Build();

    private ValueTask OnRetryAsync(ConversationRequest request, OnRetryArguments<object> args)
    {
        var delaySeconds = (int)args.RetryDelay.TotalSeconds;
        _logger.LogWarning(
            "{Provider} rate limit hit (attempt {Retry}/{MaxRetries}). Model={Model}, Retrying in {Delay}s. Reason: {Error}",
            _providerName,
            args.AttemptNumber + 1,
            request.MaxRateLimitRetries,
            request.Model,
            delaySeconds,
            args.Outcome.Exception?.Message);
        _emitRetryNotification(request.ConversationId, args.AttemptNumber + 1,
            request.MaxRateLimitRetries, delaySeconds);
        return ValueTask.CompletedTask;
    }
}
