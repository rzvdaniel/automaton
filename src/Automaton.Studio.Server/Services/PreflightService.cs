using System.Text.Json;
using Automaton.Mcp.Client;
using Automaton.Mcp.Models;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Fast single-shot LLM gate that runs before the main tool loop.
/// Decides whether the AI has enough context to proceed with changes,
/// or should ask the user for clarification first.
/// Modes (agent/plan) are always user-controlled — this service never switches them.
/// </summary>
public class PreflightService(IAiProviderRegistry registry, ILogger<PreflightService> logger)
{
    private const string SystemPrompt = """
        Analyze the user request and decide: proceed immediately, or ask for clarification first?

        Respond with ONLY valid JSON — no markdown, no code fences, no explanation:
        {"action":"proceed"}
        OR
        {"action":"clarify","message":"<one focused question>"}

        Rules:
        - Questions, explanations, analysis → always "proceed" (answering does not touch files)
        - Clear, specific change requests → "proceed"
        - Vague change requests where a wrong assumption would waste the user's time → "clarify"
        - Be generous with "proceed" — the bar for asking is high
        - If clarifying, ask exactly ONE focused question
        """;

    public async Task<PreflightResult> CheckAsync(
        string provider,
        string model,
        string apiKey,
        string userMessage,
        IEnumerable<ConversationMessage>? recentHistory = null,
        CancellationToken ct = default)
    {
        var aiProvider = registry.GetProvider(provider);
        if (aiProvider == null)
        {
            logger.LogWarning("Pre-flight: provider '{Provider}' not found — proceeding", provider);
            return PreflightResult.GoAhead;
        }

        // Include the last 3 exchanges (6 messages) for context
        var context = recentHistory?
            .TakeLast(6)
            .Select(m => $"{m.Role}: {(m.Content.Length > 300 ? m.Content[..300] + "…" : m.Content)}")
            .ToList();

        var prompt = context?.Count > 0
            ? $"Recent conversation:\n{string.Join("\n", context)}\n\nNew request: {userMessage}"
            : userMessage;

        var request = new ConversationRequest
        {
            Provider      = provider,
            Model         = model,
            ApiKey        = apiKey,
            InitialMessage = prompt,
            SystemPrompt  = SystemPrompt,
            MaxIterations = 1,
            MaxConsecutiveFailures = 1
        };

        try
        {
            var raw = await aiProvider.CallAsync(
                request,
                [],
                (_, _, _) => Task.FromResult(string.Empty),
                ct);

            // Strip markdown code fences if the model wraps output anyway
            var json = raw.Trim();
            if (json.StartsWith("```"))
                json = string.Join('\n', json.Split('\n').Skip(1).SkipLast(1)).Trim();

            using var doc  = JsonDocument.Parse(json);
            var action     = doc.RootElement.GetProperty("action").GetString();

            if (action == "clarify" &&
                doc.RootElement.TryGetProperty("message", out var msgProp) &&
                msgProp.GetString() is string msg && !string.IsNullOrWhiteSpace(msg))
            {
                logger.LogInformation("Pre-flight: clarify — {Message}", msg);
                return new PreflightResult(Proceed: false, ClarifyMessage: msg);
            }

            return PreflightResult.GoAhead;
        }
        catch (Exception ex)
        {
            // Fail open — a broken pre-flight should never block the user
            logger.LogWarning(ex, "Pre-flight check failed — proceeding");
            return PreflightResult.GoAhead;
        }
    }
}

/// <param name="Proceed">True to continue to the main tool loop; false to send <see cref="ClarifyMessage"/> and stop.</param>
/// <param name="ClarifyMessage">The question to send to the user when <see cref="Proceed"/> is false.</param>
public record PreflightResult(bool Proceed, string? ClarifyMessage = null)
{
    public static readonly PreflightResult GoAhead = new(true);
}
