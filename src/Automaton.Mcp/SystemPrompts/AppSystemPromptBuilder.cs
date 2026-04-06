using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Service for building AI system prompts for app generation, with tier-based template selection.
/// </summary>
public class AppSystemPromptBuilder : IAppSystemPromptBuilder
{
    private const string VerificationStrategyEnvVar = "AUTOMATON_APP_VERIFICATION_STRATEGY";

    // Tiered prompts — base is always prepended to each tier
    private static readonly Lazy<string> BasePrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.AppPrompts.app-base.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> SimplePrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.AppPrompts.app-simple.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> MediumPrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.AppPrompts.app-medium.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> LargePrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.AppPrompts.app-large.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> EnterprisePrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.AppPrompts.app-enterprise.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> PlanningPrompt = new(
        () => LoadEmbeddedResource("Automaton.Mcp.SystemPrompts.AppPrompts.app-planning.md"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IStepSystemPromptBuilder _stepPromptBuilder;
    private readonly IReadOnlyDictionary<string, IAppVerificationPromptStrategy> _verificationStrategies;

    public AppSystemPromptBuilder(
        IStepSystemPromptBuilder stepPromptBuilder,
        IEnumerable<IAppVerificationPromptStrategy> verificationStrategies)
    {
        _stepPromptBuilder = stepPromptBuilder;
        _verificationStrategies = verificationStrategies
            .GroupBy(strategy => strategy.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Build a system prompt for the given complexity tier.
    /// </summary>
    public string BuildSystemPrompt(ComplexityTier? tier = null)
    {
        var tierPrompt = tier switch
        {
            ComplexityTier.Simple     => SimplePrompt.Value,
            ComplexityTier.Medium     => MediumPrompt.Value,
            ComplexityTier.Large      => LargePrompt.Value,
            ComplexityTier.Enterprise => EnterprisePrompt.Value,
            _                         => MediumPrompt.Value
        };

        var verificationBlock = ResolveVerificationPromptBlock();
        return $"{BasePrompt.Value}\n\n{tierPrompt}\n\n{verificationBlock}";
    }

    /// <summary>
    /// Wraps <paramref name="baseSystemPrompt"/> with the planning-phase instruction block
    /// loaded from <c>app-planning.md</c>.
    /// </summary>
    public string BuildPlanningPhaseSystemPrompt(string baseSystemPrompt) =>
        $"{baseSystemPrompt}\n\n{PlanningPrompt.Value}";

    private string ResolveVerificationPromptBlock()
    {
        var requested = Environment.GetEnvironmentVariable(VerificationStrategyEnvVar);
        var resolvedName = string.IsNullOrWhiteSpace(requested)
            ? AppVerificationStrategyNames.Balanced
            : requested.Trim();

        if (_verificationStrategies.TryGetValue(resolvedName, out var strategy))
        {
            return strategy.BuildPromptBlock();
        }

        if (_verificationStrategies.TryGetValue(AppVerificationStrategyNames.Balanced, out var balanced))
        {
            return balanced.BuildPromptBlock();
        }

        return
            """
## VERIFICATION STRATEGY
- Use focused verification and ensure runtime errors are fixed before completion.
""";
    }

    private static string LoadEmbeddedResource(string resourceName)
    {
        var assembly = typeof(AppSystemPromptBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
