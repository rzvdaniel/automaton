using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Service for building AI system prompts for app generation, with tier-based template selection.
/// </summary>
public class AppSystemPromptBuilder : IAppSystemPromptBuilder
{
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

    private readonly IStepSystemPromptBuilder _stepPromptBuilder;

    public AppSystemPromptBuilder(IStepSystemPromptBuilder stepPromptBuilder)
    {
        _stepPromptBuilder = stepPromptBuilder;
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

        return $"{BasePrompt.Value}\n\n{tierPrompt}";
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
