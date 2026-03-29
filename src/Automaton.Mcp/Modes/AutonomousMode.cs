using System.IO;
using System.Reflection;
using System.Threading;
using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Autonomous mode: AI implements the entire task from start to finish without pausing
/// for user approval. No plan review, no stage-by-stage confirmation.
/// Iteration limit defaults to <see cref="IterationLimits.Autonomous"/> (500) unless
/// overridden by admin configuration.
/// </summary>
public sealed class AutonomousMode : IConversationMode
{
    private const string PromptResourceName = "Automaton.Mcp.SystemPrompts.ModePrompts.autonomous.md";
    private static readonly Lazy<string> CachedPrompt = new(LoadPrompt, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Name => ConversationModes.Autonomous;

    public string SystemPromptSuffix => $"\n\n{CachedPrompt.Value}";

    public bool IsToolAllowed(McpTool tool) => true;

    private static string LoadPrompt()
    {
        var assembly = typeof(AutonomousMode).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{PromptResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
