using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Automaton.Mcp.SystemPrompts;

/// <summary>
/// Service for building unified AI system prompts that generate both frontend and backend code
/// </summary>
public class AppSystemPromptBuilder : IAppSystemPromptBuilder
{
    private const string PromptResourceName = "Automaton.Mcp.SystemPrompts.AppSystemPrompt.md";
    private static readonly Lazy<string> CachedPrompt = new(LoadPrompt, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly IStepSystemPromptBuilder _stepPromptBuilder;

    public AppSystemPromptBuilder(IStepSystemPromptBuilder stepPromptBuilder)
    {
        _stepPromptBuilder = stepPromptBuilder;
    }

    /// <summary>
    /// Build a unified system prompt for combined frontend + backend generation
    /// </summary>
    public string BuildSystemPrompt()
    {
        return CachedPrompt.Value;
    }

    private static string LoadPrompt()
    {
        var assembly = typeof(AppSystemPromptBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{PromptResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
