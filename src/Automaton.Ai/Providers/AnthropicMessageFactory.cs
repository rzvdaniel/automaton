using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using Tool = Anthropic.SDK.Common.Tool;

namespace Automaton.Ai.Providers;

internal sealed class AnthropicMessageFactory
{
    private const int MaxConversationHistoryMessages = 10;
    private readonly ILogger _logger;

    public AnthropicMessageFactory(ILogger logger) => _logger = logger;

    public List<Message> BuildMessages(ConversationRequest request)
    {
        var messages = new List<Message>();
        var history = request.ConversationHistory;
        if (history is { Count: > MaxConversationHistoryMessages })
        {
            var totalMessages = history.Count;
            var skipCount = history.Count - MaxConversationHistoryMessages;
            history = history.Skip(skipCount).ToList();
            _logger.LogDebug(
                "Conversation history truncated: {TotalMessages} messages -> {KeptMessages} messages (sliding window)",
                totalMessages,
                history.Count);
        }

        if (history is not null)
        {
            foreach (var message in history)
            {
                var role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? RoleType.Assistant
                    : RoleType.User;
                messages.Add(new Message
                {
                    Role = role,
                    Content = new List<ContentBase> { new TextContent { Text = message.Content } }
                });
            }
        }

        messages.Add(new Message
        {
            Role = RoleType.User,
            Content = new List<ContentBase> { new TextContent { Text = request.InitialMessage } }
        });
        return messages;
    }

    public List<Tool> BuildTools(IEnumerable<ProviderTool> tools)
    {
        var anthropicTools = new List<Tool>();
        foreach (var tool in tools)
        {
            try
            {
                var schema = ParseInputSchema(tool);
                anthropicTools.Add(new Tool(new Function(tool.Name, tool.Description, schema)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create tool {ToolName}", tool.Name);
            }
        }

        return anthropicTools;
    }

    private JsonNode? ParseInputSchema(ProviderTool tool)
    {
        const string emptySchema = "{\"type\":\"object\",\"properties\":{}}";
        if (string.IsNullOrEmpty(tool.InputSchemaJson) || tool.InputSchemaJson == "{}")
            return JsonNode.Parse(emptySchema);

        try
        {
            return JsonNode.Parse(tool.InputSchemaJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse input schema for tool {ToolName}", tool.Name);
            return JsonNode.Parse(emptySchema);
        }
    }
}
