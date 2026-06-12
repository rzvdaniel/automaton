using System.Text.Json;

namespace Automaton.Ai.Providers;

/// <summary>
/// Shared helpers used across all AI provider implementations.
/// </summary>
internal static class ProviderHelpers
{
    /// <summary>
    /// Returns true when a tool result signals that the tool loop must pause for user approval.
    /// Any tool can signal this by including <c>"awaitingApproval": true</c> in its JSON result.
    /// Currently used by <c>propose_plan</c> (new plans and high-complexity stage transitions)
    /// and <c>delete_file</c> (irreversible deletion preview).
    /// </summary>
    internal static bool ToolResultAwaitsApproval(string toolName, string toolResult)
    {
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
