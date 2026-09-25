using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using Polly;
using Polly.Retry;
using System.ClientModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Automaton.Ai.Providers;

/// <summary>
/// Provider for any OpenAI Chat Completions-compatible API (Moonshot, Groq, Together, Mistral, etc.).
/// Configure once per compatible provider — only <paramref name="providerName"/>, <paramref name="baseUrl"/>,
/// and <paramref name="defaultModel"/> differ between providers.
/// </summary>
public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly int _maxConversationHistoryMessages;
    private readonly OpenAiCompatibleStreamReader _streamReader;
    private readonly OpenAiCompatibleRequestFactory _requestFactory;
    private readonly OpenAiCompatibleEventEmitter _eventEmitter;
    private readonly OpenAiCompatibleStreamRetryExecutor _streamRetryExecutor;

    public string ProviderName { get; }
    public string DefaultModel { get; }

    public event EventHandler<ConversationUpdatedEvent>? ConversationUpdated;

    /// <param name="providerName">Unique name matching AiProvider.Name in database (e.g. "Moonshot").</param>
    /// <param name="baseUrl">Base URL of the provider's OpenAI-compatible API (e.g. "https://api.moonshot.cn/v1").</param>
    /// <param name="defaultModel">Default model ID when ConversationRequest.Model is null.</param>
    /// <param name="logger">Logger instance.</param>
    public OpenAiCompatibleProvider(
        string providerName,
        string baseUrl,
        string defaultModel,
        ILogger logger,
        int maxConversationHistoryMessages = 30)
    {
        ProviderName = providerName;
        DefaultModel = defaultModel;
        _baseUrl = baseUrl.TrimEnd('/');
        _logger = logger;
        _maxConversationHistoryMessages = maxConversationHistoryMessages > 0 ? maxConversationHistoryMessages : 30;
        _streamReader = new OpenAiCompatibleStreamReader(updated => ConversationUpdated?.Invoke(this, updated));
        _requestFactory = new OpenAiCompatibleRequestFactory(_baseUrl, _maxConversationHistoryMessages);
        _eventEmitter = new OpenAiCompatibleEventEmitter(updated => ConversationUpdated?.Invoke(this, updated));
        _streamRetryExecutor = new OpenAiCompatibleStreamRetryExecutor(
            logger, _streamReader, providerName, _eventEmitter.EmitRetryNotification);
    }

    public async Task<ProviderResult> CallAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CallChatCompletionsAsync(request, tools, executeToolAsync, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("{Provider} conversation cancelled by client", ProviderName);
            throw;
        }
        catch (ClientResultException crex)
        {
            // Extract the full response body from the raw HTTP response so the actual API error
            // (e.g. "model not found", "tools not supported") is visible in logs.
            // crex.Message is only the SDK generic string; the real detail is in the response body.
            string? rawBody = null;
            try { rawBody = crex.GetRawResponse()?.Content.ToString(); } catch { /* ignore */ }
            _logger.LogError(
                "{Provider} API error {Status} for model '{Model}'. KeyLength={KeyLen}. Body: {Body}",
                ProviderName,
                crex.Status,
                request.Model,
                request.ApiKey?.Length ?? 0,
                rawBody ?? crex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Provider} Error calling {Provider} API for model '{Model}'", ProviderName, ProviderName, request.Model);
            throw;
        }
    }

    private async Task<ProviderResult> CallChatCompletionsAsync(
        ConversationRequest request,
        IEnumerable<ProviderTool> tools,
        Func<string, string, string, Task<string>> executeToolAsync,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrEmpty(request.Model) ? DefaultModel : request.Model;
        var apiKey = request.ApiKey ?? string.Empty;

        var client = _requestFactory.BuildChatClient(model, apiKey, request.ExtraBodyJson);

        var toolList = tools.ToList();
        var chatTools = OpenAiCompatibleRequestFactory.BuildChatTools(toolList);

        var messages = _requestFactory.BuildMessages(request);

        var maxIterations = ProviderIterationLimits.Resolve(request.MaxIterations);
        var maxConsecutiveFailures = request.MaxConsecutiveFailures > 0 ? request.MaxConsecutiveFailures : 3;
        var iterationCount = 0;
        var consecutiveFailures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var responseBuilder = new StringBuilder();

        var tokenUsage = new TokenUsageAccumulator();

        // When propose_plan returns awaitingApproval, suppress tools on the next iteration
        // so the LLM can present the plan and ask for user confirmation.
        var breakAfterPlanPresentation = false;

        while (true)
        {
            iterationCount++;
            if (TryStopAtIterationLimit(iterationCount, maxIterations, responseBuilder))
                break;

            var options = OpenAiCompatibleRequestFactory.BuildCompletionOptions(request, chatTools, breakAfterPlanPresentation);
            var streamResult = await _streamRetryExecutor.ExecuteAsync(
                request, client, messages, options, cancellationToken);
            // Accumulate tokens across all iterations — input, output, and cache are all per-call values.
            // Tokens are only emitted once on the final turn so the server accumulates the full billing total.
            tokenUsage.Add(streamResult.inputTokens, streamResult.outputTokens, cacheReadTokens: streamResult.cacheReadTokens);

            var finalText = streamResult.finalText;
            var reasoningText = streamResult.reasoningText;
            var reasoningFieldName = streamResult.reasoningFieldName;
            var toolCalls = streamResult.toolCalls;
            var responseModel = streamResult.responseModel ?? model;

            if (toolCalls.Count == 0)
            {
                EmitFinalResponse(request, finalText, reasoningText, responseModel, tokenUsage, responseBuilder);
                break;
            }

            EmitPartialResponse(request, finalText, reasoningText, responseModel);
            messages.Add(OpenAiCompatibleRequestFactory.BuildAssistantToolCallMessage(
                finalText, reasoningText, reasoningFieldName, toolCalls));

            var toolOutcome = await ExecuteToolCallsAsync(
                request, toolCalls, executeToolAsync, consecutiveFailures, maxConsecutiveFailures, messages);
            if (toolOutcome.ShouldStop)
            {
                StopAfterRepeatedToolFailure(
                    request, finalText, reasoningText, responseModel, tokenUsage, responseBuilder);
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

    private bool TryStopAtIterationLimit(int iterationCount, int maxIterations, StringBuilder responseBuilder)
    {
        if (iterationCount <= maxIterations)
            return false;
        _logger.LogWarning("{Provider} exceeded maximum iterations ({Max}), stopping", ProviderName, maxIterations);
        responseBuilder.AppendLine("\n\n[Conversation stopped: Maximum tool call iterations reached]");
        return true;
    }

    private void StopAfterRepeatedToolFailure(
        ConversationRequest request,
        string finalText,
        string reasoningText,
        string responseModel,
        TokenUsageAccumulator tokenUsage,
        StringBuilder responseBuilder)
    {
        if (string.IsNullOrWhiteSpace(finalText) && tokenUsage.HasAny)
            _eventEmitter.EmitStreamingText(request.ConversationId, string.Empty, isComplete: true,
                inputTokens: tokenUsage.InputTokens,
                outputTokens: tokenUsage.OutputTokensOrNull,
                model: responseModel,
                reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                cacheReadTokens: tokenUsage.CacheReadTokens);
        responseBuilder.AppendLine("\n\n[Conversation stopped: Tool repeatedly failed]");
    }

    private void EmitFinalResponse(
        ConversationRequest request,
        string finalText,
        string reasoningText,
        string responseModel,
        TokenUsageAccumulator tokenUsage,
        StringBuilder responseBuilder)
    {
        LogCacheStats(tokenUsage.CacheReadTokens, tokenUsage.InputTokens);
        if (!string.IsNullOrWhiteSpace(finalText))
        {
            _eventEmitter.EmitStreamingText(request.ConversationId, finalText, isComplete: true,
                inputTokens: tokenUsage.InputTokens,
                outputTokens: tokenUsage.OutputTokensOrNull,
                model: responseModel,
                reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                cacheReadTokens: tokenUsage.CacheReadTokens);
            responseBuilder.AppendLine(finalText);
        }
        else if (tokenUsage.HasAny)
        {
            _eventEmitter.EmitStreamingText(request.ConversationId, string.Empty, isComplete: true,
                inputTokens: tokenUsage.InputTokens,
                outputTokens: tokenUsage.OutputTokensOrNull,
                model: responseModel,
                reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                cacheReadTokens: tokenUsage.CacheReadTokens);
        }
    }

    private void EmitPartialResponse(
        ConversationRequest request,
        string finalText,
        string reasoningText,
        string responseModel)
    {
        if (!string.IsNullOrWhiteSpace(finalText))
            _eventEmitter.EmitStreamingText(request.ConversationId, finalText, isComplete: true,
                inputTokens: null,
                outputTokens: null,
                model: responseModel,
                reasoningContent: string.IsNullOrEmpty(reasoningText) ? null : reasoningText,
                cacheReadTokens: null);
    }

    private async Task<CompatibleToolOutcome> ExecuteToolCallsAsync(
        ConversationRequest request,
        List<OpenAiCompatibleStreamReader.ToolCallAccumulator> toolCalls,
        Func<string, string, string, Task<string>> executeToolAsync,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures,
        List<ChatMessage> messages)
    {
        var tasks = toolCalls.Select(async call =>
        {
            var arguments = call.ArgumentsBuilder.ToString();
            if (string.IsNullOrEmpty(arguments))
                arguments = "{}";
            _eventEmitter.EmitToolCallStarted(request.ConversationId, call.Id, call.FunctionName, arguments);
            var result = await executeToolAsync(call.Id, call.FunctionName, arguments);
            _eventEmitter.EmitToolCallCompleted(request.ConversationId, call.Id, call.FunctionName, arguments, result);
            return (call, result);
        });

        var results = await Task.WhenAll(tasks);
        var shouldStop = false;
        var shouldBreakForApproval = false;
        foreach (var (call, result) in results)
        {
            shouldStop |= UpdateConsecutiveFailures(call.FunctionName, result, consecutiveFailures, maxConsecutiveFailures);
            shouldBreakForApproval |= ProviderHelpers.ToolResultAwaitsApproval(call.FunctionName, result);
            messages.Add(new ToolChatMessage(call.Id, result));
        }
        return new CompatibleToolOutcome(shouldStop, shouldBreakForApproval);
    }

    private bool UpdateConsecutiveFailures(
        string toolName,
        string result,
        Dictionary<string, int> consecutiveFailures,
        int maxConsecutiveFailures)
    {
        if (!IsToolFailure(result))
        {
            consecutiveFailures[toolName] = 0;
            return false;
        }

        consecutiveFailures.TryGetValue(toolName, out var count);
        consecutiveFailures[toolName] = count + 1;
        if (consecutiveFailures[toolName] < maxConsecutiveFailures)
            return false;
        _logger.LogWarning("Tool {Tool} failed {Count} consecutive times, stopping", toolName, consecutiveFailures[toolName]);
        return true;
    }

    private static bool IsToolFailure(string result) =>
        result.StartsWith("Tool execution failed:") ||
        result.StartsWith("Error executing tool") ||
        result.Contains("A second operation was started");

    private sealed record CompatibleToolOutcome(bool ShouldStop, bool ShouldBreakForApproval);

    private void LogCacheStats(int? cacheReadTokens, int? totalInputTokens)
    {
        if (cacheReadTokens > 0)
            _logger.LogDebug(
                "{Provider} cache hit — read: {CacheRead} tokens (saved ~{Saved} tokens from billing)",
                ProviderName, cacheReadTokens, cacheReadTokens);
        else
            _logger.LogDebug("{Provider} cache stats — no cache hit this request (input: {Input} tokens)",
                ProviderName, totalInputTokens ?? 0);
    }

}
