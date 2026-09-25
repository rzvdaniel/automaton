using Automaton.Ai.Models;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class OpenAiCompatibleRequestFactory
{
    private readonly string _baseUrl;
    private readonly int _maxConversationHistoryMessages;

    public OpenAiCompatibleRequestFactory(string baseUrl, int maxConversationHistoryMessages)
    {
        _baseUrl = baseUrl;
        _maxConversationHistoryMessages = maxConversationHistoryMessages;
    }

    public ChatClient BuildChatClient(string model, string apiKey, string? extraBodyJson)
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri($"{_baseUrl}/") };
        if (!string.IsNullOrWhiteSpace(extraBodyJson))
            options.AddPolicy(new JsonBodyMergePolicy(extraBodyJson), System.ClientModel.Primitives.PipelinePosition.PerCall);
        return new OpenAIClient(new ApiKeyCredential(apiKey), options).GetChatClient(model);
    }

    public static List<ChatTool> BuildChatTools(IEnumerable<ProviderTool> tools)
    {
        var chatTools = new List<ChatTool>();
        foreach (var tool in tools)
        {
            try
            {
                chatTools.Add(ChatTool.CreateFunctionTool(
                    tool.Name, tool.Description, BinaryData.FromString(tool.InputSchemaJson)));
            }
            catch (Exception)
            {
                // Preserve the existing behavior: omit malformed tool schemas.
            }
        }
        return chatTools;
    }

    public List<ChatMessage> BuildMessages(ConversationRequest request)
    {
        var messages = new List<ChatMessage>();
        if (request.SystemPromptParts is not null)
        {
            messages.Add(new SystemChatMessage(request.SystemPromptParts.BasePrompt));
            AddSystemPrompt(messages, request.SystemPromptParts.InstructionBlock);
            AddSystemPrompt(messages, request.SystemPromptParts.MemoryBlock);
        }
        else if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(request.SystemPrompt));
        }

        AddConversationHistory(messages, request.ConversationHistory);
        messages.Add(new UserChatMessage(request.InitialMessage));
        return messages;
    }

    public static ChatCompletionOptions BuildCompletionOptions(
        ConversationRequest request,
        List<ChatTool> chatTools,
        bool breakAfterPlanPresentation)
    {
        var includeTools = request.SupportsTools && chatTools.Count > 0 && !breakAfterPlanPresentation;
        var options = includeTools ? new ChatCompletionOptions { Tools = { } } : new ChatCompletionOptions();
        if (request.MaxTokens > 0)
            options.MaxOutputTokenCount = request.MaxTokens;
        if (request.Temperature.HasValue)
            options.Temperature = request.Temperature.Value;
        if (request.TopP.HasValue)
            options.TopP = request.TopP.Value;
        if (includeTools)
            foreach (var tool in chatTools)
                options.Tools.Add(tool);
        return options;
    }

    public static ChatMessage BuildAssistantToolCallMessage(
        string finalText,
        string reasoningText,
        string? reasoningFieldName,
        List<OpenAiCompatibleStreamReader.ToolCallAccumulator> toolCalls)
    {
        if (string.IsNullOrEmpty(reasoningText))
            return BuildTypedAssistantMessage(finalText, toolCalls);

        var toolCallNodes = toolCalls.Select(call => new
        {
            id = call.Id,
            type = "function",
            function = new { name = call.FunctionName, arguments = call.ArgumentsBuilder.ToString() }
        });
        var messageJson = reasoningFieldName == "reasoning"
            ? BuildReasoningDetailsMessage(finalText, reasoningText, toolCallNodes)
            : BuildReasoningContentMessage(finalText, reasoningText, toolCallNodes);
        return System.ClientModel.Primitives.ModelReaderWriter.Read<ChatMessage>(
            BinaryData.FromString(messageJson), System.ClientModel.Primitives.ModelReaderWriterOptions.Json)!;
    }

    private static string BuildReasoningDetailsMessage(string finalText, string reasoningText, object toolCalls) =>
        JsonSerializer.Serialize(new
        {
            role = "assistant",
            reasoning_details = new[]
            {
                new Dictionary<string, string> { ["type"] = "thinking", ["thinking"] = reasoningText }
            },
            content = string.IsNullOrWhiteSpace(finalText) ? "" : finalText,
            tool_calls = toolCalls
        });

    private static string BuildReasoningContentMessage(string finalText, string reasoningText, object toolCalls) =>
        JsonSerializer.Serialize(new
        {
            role = "assistant",
            reasoning_content = reasoningText,
            content = string.IsNullOrWhiteSpace(finalText) ? "" : finalText,
            tool_calls = toolCalls
        });

    private static ChatMessage BuildTypedAssistantMessage(
        string finalText,
        List<OpenAiCompatibleStreamReader.ToolCallAccumulator> toolCalls)
    {
        var message = new AssistantChatMessage(
            toolCalls.Select(call => ChatToolCall.CreateFunctionToolCall(
                call.Id, call.FunctionName, BinaryData.FromString(call.ArgumentsBuilder.ToString()))).ToList())
        {
            Content = { }
        };
        if (!string.IsNullOrWhiteSpace(finalText))
            message.Content.Add(ChatMessageContentPart.CreateTextPart(finalText));
        return message;
    }

    private void AddConversationHistory(List<ChatMessage> messages, IReadOnlyList<ConversationMessage>? history)
    {
        if (history is not { Count: > 0 })
            return;
        var includedHistory = history.Count > _maxConversationHistoryMessages
            ? history.Skip(history.Count - _maxConversationHistoryMessages).ToList()
            : history;
        foreach (var message in includedHistory)
            messages.Add(CreateHistoryMessage(message));
    }

    private static ChatMessage CreateHistoryMessage(ConversationMessage message) =>
        message.Role.ToLowerInvariant() switch
        {
            "assistant" => new AssistantChatMessage(message.Content),
            "system" => new SystemChatMessage(message.Content),
            _ => new UserChatMessage(message.Content)
        };

    private static void AddSystemPrompt(List<ChatMessage> messages, string? promptPart)
    {
        if (!string.IsNullOrWhiteSpace(promptPart))
            messages.Add(new SystemChatMessage(promptPart));
    }

    private sealed class JsonBodyMergePolicy : System.ClientModel.Primitives.PipelinePolicy
    {
        private readonly JsonDocument _extra;

        public JsonBodyMergePolicy(string extraBodyJson) => _extra = JsonDocument.Parse(extraBodyJson);

        public override void Process(
            System.ClientModel.Primitives.PipelineMessage message,
            IReadOnlyList<System.ClientModel.Primitives.PipelinePolicy> pipeline,
            int currentIndex)
        {
            MergeBody(message);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override async ValueTask ProcessAsync(
            System.ClientModel.Primitives.PipelineMessage message,
            IReadOnlyList<System.ClientModel.Primitives.PipelinePolicy> pipeline,
            int currentIndex)
        {
            MergeBody(message);
            await ProcessNextAsync(message, pipeline, currentIndex);
        }

        private void MergeBody(System.ClientModel.Primitives.PipelineMessage message)
        {
            if (message.Request.Content is null) return;

            using var stream = new System.IO.MemoryStream();
            message.Request.Content.WriteTo(stream, message.CancellationToken);
            stream.Position = 0;
            try
            {
                using var original = JsonDocument.Parse(stream);
                using var merged = new System.IO.MemoryStream();
                using (var writer = new Utf8JsonWriter(merged))
                {
                    writer.WriteStartObject();
                    foreach (var property in original.RootElement.EnumerateObject())
                        property.WriteTo(writer);
                    foreach (var property in _extra.RootElement.EnumerateObject())
                    {
                        if (!original.RootElement.TryGetProperty(property.Name, out _))
                            property.WriteTo(writer);
                    }
                    writer.WriteEndObject();
                }
                message.Request.Content = System.ClientModel.BinaryContent.Create(
                    BinaryData.FromBytes(merged.ToArray()));
            }
            catch
            {
                // If body isn't valid JSON, send it unmodified.
            }
        }
    }
}
