using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class OpenAiResponsesApiClient
{
    private readonly ILogger _logger;
    private static readonly HttpClient ResponsesHttpClient = new()
    {
        BaseAddress = new Uri("https://api.openai.com/v1/"),
        Timeout     = System.Threading.Timeout.InfiniteTimeSpan
    };

    public OpenAiResponsesApiClient(ILogger logger) => _logger = logger;

    public async Task<ResponsesApiResult> CreateResponsesAsync(
        string model,
        string apiKey,
        int? maxTokens,
        List<object> input,
        List<Dictionary<string, object?>> tools,
        CancellationToken cancellationToken)
    {
        var responseContent = await SendResponsesRequestAsync(model, apiKey, maxTokens, input, tools, cancellationToken);
        using var responseDocument = JsonDocument.Parse(responseContent);
        return ParseResponsesResult(responseDocument.RootElement, model);
    }

    private async Task<string> SendResponsesRequestAsync(
        string model,
        string apiKey,
        int? maxTokens,
        List<object> input,
        List<Dictionary<string, object?>> tools,
        CancellationToken cancellationToken)
    {
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["input"] = input,
            ["tools"] = tools,
            ["tool_choice"] = tools.Count > 0 ? "auto" : "none"
        };
        if (maxTokens > 0) requestBody["max_output_tokens"] = maxTokens;
        using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await ResponsesHttpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) ThrowResponsesApiError(response.StatusCode, content);
        return content;
    }

    private void ThrowResponsesApiError(System.Net.HttpStatusCode statusCode, string content)
    {
        _logger.LogError("OpenAI responses API error: {Status} {Body}", statusCode, content);
        var detail = ExtractErrorDetail(content);
        throw new InvalidOperationException($"OpenAI responses API error: {statusCode} - {detail}");
    }

    private static string ExtractErrorDetail(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                    ? message.GetString() ?? content
                    : content;
        }
        catch
        {
            return content;
        }
    }

    private ResponsesApiResult ParseResponsesResult(JsonElement root, string model)
    {
        var outputItems = ReadOutputItems(root);
        var toolCalls = ReadResponseToolCalls(outputItems);
        var outputText = root.TryGetProperty("output_text", out var text)
            ? text.GetString()
            : ExtractOutputText(outputItems);
        var usage = ReadResponseUsage(root, model);
        return new ResponsesApiResult
        {
            OutputText = outputText ?? string.Empty,
            OutputItems = outputItems,
            ToolCalls = toolCalls,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            Model = usage.Model
        };
    }

    private List<JsonElement> ReadOutputItems(JsonElement root)
    {
        var items = new List<JsonElement>();
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return items;
        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("Skipping non-object output item with kind {Kind}", item.ValueKind);
                continue;
            }
            items.Add(item.Clone());
        }
        return items;
    }

    private static List<ResponseToolCall> ReadResponseToolCalls(IEnumerable<JsonElement> items)
    {
        var toolCalls = new List<ResponseToolCall>();
        foreach (var item in items)
        {
            if (!item.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "function_call", StringComparison.OrdinalIgnoreCase)) continue;
            var name = ReadStringProperty(item, "name");
            var callId = ReadStringProperty(item, "call_id");
            var id = ReadStringProperty(item, "id");
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(callId) && !string.IsNullOrEmpty(id))
                toolCalls.Add(new ResponseToolCall
                {
                    Id = id,
                    CallId = callId,
                    Name = name,
                    ArgumentsJson = ReadStringProperty(item, "arguments") ?? "{}"
                });
        }
        return toolCalls;
    }

    private static string? ReadStringProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static (int? InputTokens, int? OutputTokens, string? Model) ReadResponseUsage(JsonElement root, string model)
    {
        int? inputTokens = null;
        int? outputTokens = null;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("input_tokens", out var input)) inputTokens = input.GetInt32();
            if (usage.TryGetProperty("output_tokens", out var output)) outputTokens = output.GetInt32();
        }
        var responseModel = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() : model;
        return (inputTokens, outputTokens, responseModel);
    }

    private static string ExtractOutputText(List<JsonElement> outputItems)
    {
        var builder = new StringBuilder();
        foreach (var item in outputItems)
            if (TryGetMessageContent(item, out var content)) AppendOutputText(builder, content);
        return builder.ToString();
    }

    private static bool TryGetMessageContent(JsonElement item, out JsonElement content)
    {
        content = default;
        return item.TryGetProperty("type", out var type)
            && string.Equals(type.GetString(), "message", StringComparison.OrdinalIgnoreCase)
            && item.TryGetProperty("content", out content)
            && content.ValueKind == JsonValueKind.Array;
    }

    private static void AppendOutputText(StringBuilder builder, JsonElement content)
    {
        foreach (var part in content.EnumerateArray())
        {
            if (!part.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "output_text", StringComparison.OrdinalIgnoreCase)) continue;
            if (part.TryGetProperty("text", out var text)) builder.Append(text.GetString());
        }
    }

    internal sealed class ResponsesApiResult
    {
        public string OutputText { get; set; } = string.Empty;
        public List<JsonElement> OutputItems { get; set; } = new();
        public List<ResponseToolCall> ToolCalls { get; set; } = new();
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public string? Model { get; set; }
    }

    internal sealed class ResponseToolCall
    {
        public string Id { get; set; } = string.Empty;
        public string CallId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = "{}";
    }
}
