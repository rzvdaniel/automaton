using System.IO;
using System.Reflection;
using System.Threading;
using Automaton.Mcp.Models;

namespace Automaton.Mcp.Modes;

/// <summary>
/// Human-in-the-middle mode: AI proposes a plan for complex tasks, waits for user
/// confirmation, then implements everything in one pass.
/// </summary>
public sealed class HumanInTheMiddleMode : IConversationMode
{
    private const string PromptResourceName = "Automaton.Mcp.SystemPrompts.Assets.Mode.human-in-the-middle.md";
    private static readonly Lazy<string> CachedPrompt = new(LoadPrompt, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Name => ConversationModes.HumanInTheMiddle;

    public string SystemPromptSuffix => $"\n\n{CachedPrompt.Value}";

    public bool IsToolAllowed(McpTool tool) => true;

    private static string LoadPrompt()
    {
        var assembly = typeof(HumanInTheMiddleMode).Assembly;
        using var stream = assembly.GetManifestResourceStream(PromptResourceName)
            ?? throw new InvalidOperationException($"Embedded prompt resource '{PromptResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
