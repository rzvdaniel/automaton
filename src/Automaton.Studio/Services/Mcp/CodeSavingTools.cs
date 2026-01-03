using Automaton.Studio.Services.Mcp.ToolProviders;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Simple helper to check if a tool saves code.
/// Uses constants defined in the tool provider classes.
/// </summary>
public static class CodeSavingTools
{
    /// <summary>
    /// Checks if the given tool name is a code-saving tool.
    /// </summary>
    public static bool IsCodeSavingTool(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return false;

        return toolName.Equals(StepMcpToolProvider.SaveCodeToolName, StringComparison.OrdinalIgnoreCase) ||
               toolName.Equals(AppMcpToolProvider.SaveAppCodeToolName, StringComparison.OrdinalIgnoreCase) ||
               toolName.Equals(AppMcpToolProvider.SaveStepCodeToolName, StringComparison.OrdinalIgnoreCase);
    }
}
