using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Ai.Providers;

/// <summary>
/// Google Gemini provider implementation using the Gemini REST API with SSE streaming
/// </summary>
public class GoogleProvider : IAiProvider
{
    private readonly ILogger _logger;
    private readonly GoogleConversationEventEmitter eventEmitter;
    private readonly GeminiRequestFactory requestFactory;
    private readonly GeminiStreamingClient streamingClient;
    private readonly GeminiToolCallExecutor toolCallExecutor;

    /// <summary>
    /// Maximum number of conversation messages to keep in history (sliding window).
    /// Prevents unbounded token growth. Each turn = 2 messages (user + assistant).
    /// 10 messages = last 5 turns, typically 2,000-5,000 tokens saved per call.
    /// </summary>
    /// <inheritdoc />
    public string ProviderName => "Google";

    /// <inheritdoc />
    public string DefaultModel => "gemini-2.0-flash";

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    public GoogleProvider(ILogger logger)
    {
        _logger = logger;
        eventEmitter = new GoogleConversationEventEmitter(updated => ConversationUpdated?.Invoke(this, updated));
        requestFactory = new GeminiRequestFactory(logger);
        streamingClient = new GeminiStreamingClient(logger, requestFactory);
        toolCallExecutor = new GeminiToolCallExecutor(logger, eventEmitter);
    }

    public async Task<ProviderResult> CallAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CallGeminiAsync(request, tools, executeToolAsync, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Google Gemini conversation cancelled by client");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Google Gemini API");
            throw;
        }
    }

    private async Task<ProviderResult> CallGeminiAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        var toolList = tools.ToList();
        var contents = requestFactory.BuildContents(request);
        var responseBuilder = new StringBuilder();

        var maxIterations = ProviderIterationLimits.Resolve(request.MaxIterations);
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Accumulate billed totals across iterations and emit once on the final turn.
        var tokenUsage = new TokenUsageAccumulator();
        string? modelVersion = null;

        // When propose_plan returns awaitingApproval, suppress tools on the next iteration
        // so the LLM can present the plan and ask for user confirmation.
        var breakAfterPlanPresentation = false;

        while (true)
        {
            iterationCount++;
            if (iterationCount > maxIterations)
            {
                _logger.LogWarning("Conversation exceeded maximum iterations ({MaxIterations}), stopping", maxIterations);
                responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
                break;
            }

            var streamResult = await StreamGeminiIterationAsync(
                request, contents, toolList, breakAfterPlanPresentation, cancellationToken);
            modelVersion = streamResult.ModelVersion ?? modelVersion;

            // Accumulate tokens across all iterations — input and output are per-call values.
            // Tokens are only emitted once on the final turn so the server accumulates the full billing total.
            tokenUsage.Add(streamResult.InputTokens, streamResult.OutputTokens);

            var textContent = streamResult.Text;

            if (streamResult.FunctionCalls.Count == 0)
            {
                EmitFinalTurn(request, textContent, tokenUsage, modelVersion, responseBuilder);
                break;
            }

            EmitToolCallTurn(request, contents, streamResult, textContent, modelVersion);
            var toolOutcome = await toolCallExecutor.ExecuteAsync(
                request.ConversationId,
                streamResult.FunctionCalls,
                executeToolAsync,
                consecutiveFailures,
                maxConsecutiveFailures);
            contents.Add(BuildFunctionResponseTurn(toolOutcome.ResponseParts));

            if (toolOutcome.ShouldStop)
            {
                responseBuilder.AppendLine("\n\n[Conversation stopped: Tool repeatedly failed]");
                break;
            }

            if (toolOutcome.ShouldBreakForApproval)
            {
                breakAfterPlanPresentation = true;
                continue;
            }
        }

        return new ProviderResult(responseBuilder.ToString().Trim(), iterationCount);
    }

    private void EmitFinalTurn(
        ConversationRequest request,
        string textContent,
        TokenUsageAccumulator tokenUsage,
        string? modelVersion,
        StringBuilder responseBuilder)
    {
        if (!string.IsNullOrWhiteSpace(textContent))
        {
            eventEmitter.EmitStreamingTextUpdate(request.ConversationId, textContent, isComplete: true,
                inputTokens: tokenUsage.InputTokens,
                outputTokens: tokenUsage.OutputTokensOrNull,
                model: modelVersion ?? request.Model);
            responseBuilder.AppendLine(textContent);
        }
        else if (tokenUsage.HasAny)
        {
            eventEmitter.EmitStreamingTextUpdate(request.ConversationId, string.Empty, isComplete: true,
                inputTokens: tokenUsage.InputTokens,
                outputTokens: tokenUsage.OutputTokensOrNull,
                model: modelVersion ?? request.Model);
        }
    }

    private void EmitToolCallTurn(
        ConversationRequest request,
        List<Dictionary<string, object>> contents,
        GeminiIterationResult streamResult,
        string textContent,
        string? modelVersion)
    {
        if (!string.IsNullOrWhiteSpace(textContent))
            eventEmitter.EmitStreamingTextUpdate(request.ConversationId, textContent, isComplete: true,
                inputTokens: null, outputTokens: null, model: modelVersion ?? request.Model);

        contents.Add(new Dictionary<string, object>
        {
            ["role"] = "model",
            ["parts"] = streamResult.RawModelParts.Count > 0
                ? (object)streamResult.RawModelParts
                : new List<object>()
        });
    }

    private static Dictionary<string, object> BuildFunctionResponseTurn(List<object> responseParts) =>
        new() { ["role"] = "user", ["parts"] = responseParts };

    private async Task<GeminiIterationResult> StreamGeminiIterationAsync(
        ConversationRequest request,
        List<Dictionary<string, object>> contents,
        List<ProviderTool> toolList,
        bool breakAfterPlanPresentation,
        CancellationToken cancellationToken)
    {
        var currentText = new StringBuilder();
        var functionCalls = new List<GeminiFunctionCall>();
        var rawModelParts = new List<JsonElement>();
        int? inputTokens = null;
        int? outputTokens = null;
        string? modelVersion = null;
        var lastEmitTime = DateTime.UtcNow;
        var retryPipeline = CreateRetryPipeline(request);

        await retryPipeline.ExecuteAsync(async ct =>
        {
            currentText.Clear();
            functionCalls.Clear();
            rawModelParts.Clear();
            inputTokens = null;
            outputTokens = null;

            await foreach (var chunk in streamingClient.StreamAsync(
                request.Model ?? DefaultModel,
                request.ApiKey,
                request.SystemPromptParts,
                request.SystemPrompt,
                request.MaxTokens,
                contents,
                breakAfterPlanPresentation ? [] : toolList,
                ct))
            {
                UpdateUsage(chunk, ref inputTokens, ref outputTokens, ref modelVersion);
                EmitTextDelta(request.ConversationId, chunk.TextDelta, currentText, ref lastEmitTime);
                functionCalls.AddRange(chunk.FunctionCalls);
                rawModelParts.AddRange(chunk.RawParts);
            }
        }, cancellationToken);

        return new GeminiIterationResult(
            currentText.ToString(), functionCalls, rawModelParts,
            inputTokens, outputTokens, modelVersion);
    }

    private ResiliencePipeline CreateRetryPipeline(ConversationRequest request)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = request.MaxRateLimitRetries,
                Delay = TimeSpan.FromSeconds(request.RateLimitRetryInitialDelaySeconds),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsRateLimitException),
                DelayGenerator = static args =>
                    TryParseRetryAfter(args.Outcome.Exception, out var retryAfter)
                        ? ValueTask.FromResult<TimeSpan?>(retryAfter)
                        : ValueTask.FromResult<TimeSpan?>(null),
                OnRetry = args =>
                {
                    var delaySecs = (int)args.RetryDelay.TotalSeconds;
                    _logger.LogWarning(
                        "Gemini rate limit hit (attempt {Retry}/{MaxRetries}). Model={Model}, Retrying in {Delay}s. Reason: {Error}",
                        args.AttemptNumber + 1, request.MaxRateLimitRetries,
                        request.Model, delaySecs, args.Outcome.Exception?.Message);
                    eventEmitter.EmitRetryNotification(request.ConversationId, args.AttemptNumber + 1,
                        request.MaxRateLimitRetries, delaySecs);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    private void UpdateUsage(
        GeminiStreamChunk chunk,
        ref int? inputTokens,
        ref int? outputTokens,
        ref string? modelVersion)
    {
        if (chunk.InputTokens.HasValue) inputTokens = chunk.InputTokens;
        if (chunk.OutputTokens.HasValue) outputTokens = chunk.OutputTokens;
        if (chunk.ModelVersion is not null) modelVersion = chunk.ModelVersion;
    }

    private void EmitTextDelta(
        string conversationId,
        string? textDelta,
        StringBuilder currentText,
        ref DateTime lastEmitTime)
    {
        if (string.IsNullOrEmpty(textDelta))
            return;

        currentText.Append(textDelta);
        var now = DateTime.UtcNow;
        if ((now - lastEmitTime).TotalMilliseconds < 100)
            return;

        eventEmitter.EmitStreamingTextUpdate(conversationId, currentText.ToString(), isComplete: false);
        lastEmitTime = now;
    }

    private sealed record GeminiIterationResult(
        string Text,
        List<GeminiFunctionCall> FunctionCalls,
        List<JsonElement> RawModelParts,
        int? InputTokens,
        int? OutputTokens,
        string? ModelVersion);

    private static bool IsRateLimitException(Exception ex)
    {
        var msg = ex.Message;
        if (msg.Contains("insufficient_quota",       StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("quota_exceeded",           StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("exceeded_current_quota",   StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("insufficient balance",     StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("suspended",                StringComparison.OrdinalIgnoreCase))
            return false;

        return msg.Contains("RESOURCE_EXHAUSTED",  StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("rate_limit",           StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("429",                  StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Too Many Requests",    StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseRetryAfter(Exception? ex, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (ex is null) return false;
        var match = RetryAfterPattern.Match(ex.Message);
        if (match.Success && int.TryParse(match.Groups["secs"].Value, out var secs) && secs > 0)
        {
            retryAfter = TimeSpan.FromSeconds(secs);
            return true;
        }
        return false;
    }

    private static readonly Regex RetryAfterPattern = new(
        @"retry[- ](?:after|in)\D*(?<secs>\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

}
