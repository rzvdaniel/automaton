using System.Text.Json;
using Automaton.Mcp.Modes;
using Automaton.Mcp.Tools;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// Tracks whether propose_plan has returned an awaitingApproval result
/// and determines whether the tool loop should break based on mode.
/// Compose this into any AI provider to get consistent plan-gate behavior.
/// </summary>
public class PlanGateTracker
{
    /// <summary>True once propose_plan has returned awaitingApproval.</summary>
    public bool IsGated { get; private set; }

    /// <summary>
    /// Inspects a tool result after execution. If the tool is propose_plan
    /// and the result contains <c>awaitingApproval: true</c>, marks the gate as active.
    /// </summary>
    public void CheckToolResult(string toolName, string toolResult)
    {
        if (IsGated || toolName != PlanningTool.ProposePlanName) return;

        try
        {
            using var doc = JsonDocument.Parse(toolResult);
            if (doc.RootElement.TryGetProperty("awaitingApproval", out var aw) &&
                aw.ValueKind == JsonValueKind.True)
            {
                IsGated = true;
            }
        }
        catch { /* result is not JSON – ignore */ }
    }

    /// <summary>
    /// Returns true if the tool loop should break.
    /// In agent mode the loop stops immediately so the AI can't implement without approval.
    /// In plan mode the loop continues so the model can produce conversational feedback.
    /// </summary>
    public bool ShouldBreakLoop(string? mode) => IsGated && mode != ConversationModes.Plan;
}
