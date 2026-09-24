using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class GeminiRequestFactory
{
    private const int MaxConversationHistoryMessages = 10;
    private readonly ILogger _logger;

    public GeminiRequestFactory(ILogger logger) => _logger = logger;

    public List<Dictionary<string, object>> BuildContents(ConversationRequest request)
    {
        var contents = new List<Dictionary<string, object>>();
        var history = request.ConversationHistory;
        if (history is { Count: > MaxConversationHistoryMessages })
        {
            var total = history.Count;
            history = history.Skip(total - MaxConversationHistoryMessages).ToList();
            _logger.LogDebug(
                "Conversation history truncated: {Total} -> {Kept} messages (sliding window)",
                total,
                history.Count);
        }

        if (history is not null)
        {
            foreach (var message in history)
            {
                var role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user";
                contents.Add(new Dictionary<string, object>
                {
                    ["role"] = role,
                    ["parts"] = new List<object>
                    {
                        new Dictionary<string, object> { ["text"] = message.Content }
                    }
                });
            }
        }

        contents.Add(new Dictionary<string, object>
        {
            ["role"] = "user",
            ["parts"] = new List<object>
            {
                new Dictionary<string, object> { ["text"] = request.InitialMessage }
            }
        });
        return contents;
    }

    public Dictionary<string, object?> BuildRequestBody(
        SystemPromptParts? promptParts,
        string? systemPromptFallback,
        int? maxTokens,
        List<Dictionary<string, object>> contents,
        List<ProviderTool> tools)
    {
        var basePrompt = promptParts?.BasePrompt ?? systemPromptFallback;
        var effectiveContents = AddExtraContext(promptParts, contents);
        var requestBody = new Dictionary<string, object?> { ["contents"] = effectiveContents };
        AddSystemInstruction(requestBody, basePrompt);
        AddToolDeclarations(requestBody, tools, maxTokens);
        AddGenerationLimit(requestBody, tools, maxTokens);
        return requestBody;
    }

    private static List<Dictionary<string, object>> AddExtraContext(
        SystemPromptParts? promptParts,
        List<Dictionary<string, object>> contents)
    {
        var extraContext = BuildExtraContext(promptParts);
        if (string.IsNullOrWhiteSpace(extraContext))
            return contents;

        var result = new List<Dictionary<string, object>>
        {
            CreateTextTurn("user", extraContext),
            CreateTextTurn("model", "Understood.")
        };
        result.AddRange(contents);
        return result;
    }

    private static Dictionary<string, object> CreateTextTurn(string role, string text) =>
        new()
        {
            ["role"] = role,
            ["parts"] = new List<object> { new Dictionary<string, object> { ["text"] = text } }
        };

    private static void AddSystemInstruction(Dictionary<string, object?> requestBody, string? basePrompt)
    {
        if (string.IsNullOrWhiteSpace(basePrompt))
            return;

        requestBody["system_instruction"] = new Dictionary<string, object>
        {
            ["parts"] = new List<object> { new Dictionary<string, object> { ["text"] = basePrompt } }
        };
    }

    private static void AddToolDeclarations(
        Dictionary<string, object?> requestBody,
        List<ProviderTool> tools,
        int? maxTokens)
    {
        if (tools.Count == 0)
            return;

        var declarations = tools.Select(tool => new Dictionary<string, object?>
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = BuildParameterSchema(tool.InputSchemaJson)
        }).ToList();

        requestBody["tools"] = new List<object>
        {
            new Dictionary<string, object> { ["function_declarations"] = declarations }
        };
        requestBody["tool_config"] = new Dictionary<string, object>
        {
            ["function_calling_config"] = new Dictionary<string, object> { ["mode"] = "AUTO" }
        };
        requestBody["generationConfig"] = new Dictionary<string, object?>
        {
            ["thinkingConfig"] = new Dictionary<string, object> { ["thinkingBudget"] = 0 },
            ["maxOutputTokens"] = maxTokens > 0 ? (object?)maxTokens : null
        };
    }

    private static void AddGenerationLimit(
        Dictionary<string, object?> requestBody,
        List<ProviderTool> tools,
        int? maxTokens)
    {
        if (tools.Count > 0 || maxTokens is not > 0)
            return;

        requestBody["generationConfig"] = new Dictionary<string, object?> { ["maxOutputTokens"] = maxTokens };
    }

    private static string? BuildExtraContext(SystemPromptParts? parts)
    {
        if (parts is null)
            return null;

        var segments = new[] { parts.InstructionBlock, parts.MemoryBlock }
            .Where(segment => !string.IsNullOrWhiteSpace(segment));
        var joined = string.Join("\n\n", segments);
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static object BuildParameterSchema(string inputSchemaJson)
    {
        if (string.IsNullOrEmpty(inputSchemaJson) || inputSchemaJson == "{}")
            return EmptyParameterSchema();

        try
        {
            using var document = JsonDocument.Parse(inputSchemaJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyParameterSchema();
        }
    }

    private static Dictionary<string, object> EmptyParameterSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>()
    };
}
