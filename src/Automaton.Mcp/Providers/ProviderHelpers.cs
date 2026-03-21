using System.Text.Json;
using Automaton.Mcp.Tools;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// Shared helpers used across all AI provider implementations.
/// </summary>
internal static class ProviderHelpers
{
    /// <summary>
    /// Returns true when a tool result signals that the tool loop must pause for user approval.
    /// Currently this applies to <c>propose_plan</c> when <c>awaitingApproval: true</c> is returned —
    /// either for a new plan submission or when transitioning into a high-complexity stage.
    /// </summary>
    internal static bool ToolResultAwaitsApproval(string toolName, string toolResult)
    {
        if (toolName != PlanningTool.ProposePlanName) return false;

        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            return doc.RootElement.TryGetProperty("awaitingApproval", out var aw) &&
                   aw.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }
}
