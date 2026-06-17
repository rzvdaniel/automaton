using System.Text.Json;
using Automaton.Mcp.SystemPrompts;

namespace Automaton.Api.Services;

/// <summary>
/// Uses the system AI service to classify the complexity of a user's app request,
/// selecting the most appropriate prompt family tier.
/// </summary>
public class PromptClassifierService : IPromptClassifierService
{
    private const double LowConfidenceThreshold = 0.6;

    private static readonly string SystemPrompt = """
        You are a complexity classifier for a web app generation system.
        Given a user's request, classify it into exactly one of four tiers:
        - Simple: landing pages, simple forms, static content, trivial prototypes with no or minimal backend
        - Medium: dashboards with a few API calls, simple CRUD operations (1-2 resources)
        - Large: multi-feature apps (e-commerce, booking, task managers), feature-based layout, 3+ controllers
        - Enterprise: CRM/ERP-grade apps, many modules, complex state, multiple integrated systems

        Respond with ONLY valid JSON in this exact format (no markdown, no explanation):
        {"tier": "Simple|Medium|Large|Enterprise", "confidence": 0.0-1.0, "rationale": "brief reason"}
        """;

    private readonly ISystemAiService _systemAiService;
    private readonly ILogger<PromptClassifierService> _logger;

    public PromptClassifierService(ISystemAiService systemAiService, ILogger<PromptClassifierService> logger)
    {
        _systemAiService = systemAiService;
        _logger = logger;
    }

    public async Task<ClassificationResult> ClassifyAsync(string userMessage, string userId)
    {
        if (!_systemAiService.IsAvailable)
        {
            _logger.LogDebug("System AI not available, defaulting to Simple tier");
            return new ClassificationResult(ComplexityTier.Simple, 1.0, "System AI unavailable — defaulted to Simple");
        }

        try
        {
            var response = await _systemAiService.CompleteAsync(userMessage, SystemPrompt, userId);
            return ParseResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Complexity classification failed, defaulting to Simple tier");
            return new ClassificationResult(ComplexityTier.Simple, 1.0, "Classification error — defaulted to Simple");
        }
    }

    private ClassificationResult ParseResponse(string response)
    {
        try
        {
            // Strip potential markdown code fences
            var json = response.Trim();
            if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
            if (json.EndsWith("```")) json = json[..json.LastIndexOf("```")];
            json = json.Trim();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tierStr = root.GetProperty("tier").GetString() ?? "Simple";
            var confidence = root.GetProperty("confidence").GetDouble();
            var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "";

            if (!Enum.TryParse<ComplexityTier>(tierStr, ignoreCase: true, out var tier))
            {
                _logger.LogWarning("Unknown tier '{Tier}' in classifier response, defaulting to Simple", tierStr);
                tier = ComplexityTier.Simple;
            }

            // Fall back to Simple on low confidence
            if (confidence < LowConfidenceThreshold)
            {
                _logger.LogDebug("Low confidence {Confidence} for tier {Tier}, falling back to Simple", confidence, tier);
                return new ClassificationResult(ComplexityTier.Simple, confidence, $"Low confidence fallback from {tier}: {rationale}");
            }

            _logger.LogDebug("Classified as {Tier} (confidence={Confidence}): {Rationale}", tier, confidence, rationale);
            return new ClassificationResult(tier, confidence, rationale);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse classifier response: {Response}", response);
            return new ClassificationResult(ComplexityTier.Simple, 1.0, "Parse error — defaulted to Simple");
        }
    }
}
