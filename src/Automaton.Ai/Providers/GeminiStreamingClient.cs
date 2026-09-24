using Automaton.Ai.Models;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Automaton.Ai.Providers;

internal sealed class GeminiStreamingClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };
    private readonly ILogger _logger;
    private readonly GeminiRequestFactory _requestFactory;

    public GeminiStreamingClient(ILogger logger, GeminiRequestFactory requestFactory)
    {
        _logger = logger;
        _requestFactory = requestFactory;
    }

    public async IAsyncEnumerable<GeminiStreamChunk> StreamAsync(
        string model,
        string apiKey,
        SystemPromptParts? promptParts,
        string? systemPromptFallback,
        int? maxTokens,
        List<Dictionary<string, object>> contents,
        List<ProviderTool> tools,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var body = _requestFactory.BuildRequestBody(promptParts, systemPromptFallback, maxTokens, contents, tools);
        using var requestContent = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var url = new Uri(
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?key={Uri.EscapeDataString(apiKey)}&alt=sse");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = requestContent };
        using var response = await HttpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw await CreateGeminiErrorAsync(response, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await foreach (var chunk in ReadGeminiEventsAsync(stream, cancellationToken))
            yield return chunk;
    }

    private async Task<InvalidOperationException> CreateGeminiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError("Gemini streaming API error: {Status} {Body}", response.StatusCode, errorBody);
        var detail = TryGetGeminiErrorMessage(errorBody) ?? errorBody;
        return new InvalidOperationException($"Gemini API error: {response.StatusCode} - {detail}");
    }

    private static string? TryGetGeminiErrorMessage(string errorBody)
    {
        try
        {
            using var document = JsonDocument.Parse(errorBody);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
                return message.GetString();
        }
        catch (JsonException)
        {
            // Use the raw response body when the server returns non-JSON content.
        }

        return null;
    }

    private async IAsyncEnumerable<GeminiStreamChunk> ReadGeminiEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                yield break;

            var parsedLine = ParseGeminiEventLine(line);
            if (parsedLine.IsDone)
                yield break;
            if (parsedLine.Chunk is { } chunk)
                yield return chunk;
        }
    }

    private GeminiEventLine ParseGeminiEventLine(string line)
    {
        if (!line.StartsWith("data: ", StringComparison.Ordinal))
            return GeminiEventLine.Ignored;

        var jsonData = line.Substring(6).Trim();
        if (string.IsNullOrEmpty(jsonData) || jsonData == "[DONE]")
            return GeminiEventLine.Done;

        try
        {
            using var document = JsonDocument.Parse(jsonData);
            return new GeminiEventLine(ParseChunk(document.RootElement), false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini SSE chunk: {Data}", jsonData);
            return GeminiEventLine.Ignored;
        }
    }

    private static GeminiStreamChunk ParseChunk(JsonElement root)
    {
        var chunk = new GeminiStreamChunk();
        ReadUsage(root, chunk);
        if (root.TryGetProperty("modelVersion", out var modelVersion))
            chunk.ModelVersion = modelVersion.GetString();
        if (TryGetCandidateParts(root, out var parts))
            foreach (var part in parts.EnumerateArray())
                AddPart(chunk, part);
        return chunk;
    }

    private static void ReadUsage(JsonElement root, GeminiStreamChunk chunk)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage))
            return;
        if (usage.TryGetProperty("promptTokenCount", out var input))
            chunk.InputTokens = input.GetInt32();

        var candidateTokens = usage.TryGetProperty("candidatesTokenCount", out var candidateOutput)
            ? candidateOutput.GetInt32()
            : 0;
        var thoughtTokens = usage.TryGetProperty("thoughtsTokenCount", out var thoughts)
            ? thoughts.GetInt32()
            : 0;
        if (candidateTokens > 0 || thoughtTokens > 0)
            chunk.OutputTokens = candidateTokens + thoughtTokens;
    }

    private static bool TryGetCandidateParts(JsonElement root, out JsonElement parts)
    {
        parts = default;
        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            return false;

        var candidate = candidates[0];
        return candidate.TryGetProperty("content", out var content) &&
               content.TryGetProperty("parts", out parts) &&
               parts.ValueKind == JsonValueKind.Array;
    }

    private static void AddPart(GeminiStreamChunk chunk, JsonElement part)
    {
        chunk.RawParts.Add(part.Clone());
        if (part.TryGetProperty("text", out var text))
        {
            if (!IsThoughtPart(part))
                chunk.TextDelta = text.GetString() ?? string.Empty;
            return;
        }

        if (part.TryGetProperty("functionCall", out var functionCall))
            chunk.FunctionCalls.Add(CreateFunctionCall(functionCall));
    }

    private static bool IsThoughtPart(JsonElement part) =>
        part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True;

    private static GeminiFunctionCall CreateFunctionCall(JsonElement functionCall) => new()
    {
        Name = functionCall.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
        Args = functionCall.TryGetProperty("args", out var arguments) ? arguments.Clone() : default
    };

    private sealed record GeminiEventLine(GeminiStreamChunk? Chunk, bool IsDone)
    {
        public static GeminiEventLine Ignored { get; } = new(null, false);
        public static GeminiEventLine Done { get; } = new(null, true);
    }
}

internal sealed class GeminiStreamChunk
{
    public string? TextDelta { get; set; }
    public List<GeminiFunctionCall> FunctionCalls { get; set; } = new();
    public List<JsonElement> RawParts { get; set; } = new();
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? ModelVersion { get; set; }
}

internal sealed class GeminiFunctionCall
{
    public string Name { get; set; } = string.Empty;
    public JsonElement Args { get; set; }
}
