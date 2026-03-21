using System.Text.Json;
using Automaton.Mcp.Modes;
using Automaton.Mcp.Tools;

namespace Automaton.Mcp.Client.Providers;

/// <summary>
/// Tracks whether a planning tool result should halt the tool loop
/// and determines whether the tool loop should break based on mode.
/// Compose this into any AI provider to get consistent plan-gate behavior.
/// </summary>
public class PlanGateTracker
{
    /// <summary>True once a gate condition has been triggered.</summary>
    public bool IsGated { get; private set; }

    /// <summary>
    /// Inspects a tool result after execution and activates the gate when:
    /// - <c>request_plan</c> is called (always halts — AI must stop and let the system switch to plan mode)
    /// - <c>propose_plan</c> returns <c>awaitingApproval: true</c>
    /// </summary>
    public void CheckToolResult(string toolName, string toolResult)
    {
        if (IsGated) return;

        // request_plan and assess_task (with recommendation:"plan") both signal mode switch — always halt.
        if (toolName == PlanningTool.RequestPlanName || toolName == AssessmentTool.AssessTaskName)
        {
            // For assess_task, only gate when the result contains requestPlan:true
            // (i.e., recommendation was "plan"). A "proceed" result must not break the loop.
            if (toolName == AssessmentTool.AssessTaskName)
            {
                try
                {
                    using var doc = JsonDocument.Parse(toolResult);
                    if (!doc.RootElement.TryGetProperty("requestPlan", out var rp) ||
                        rp.ValueKind != JsonValueKind.True)
                        return; // recommendation was "proceed" — continue the loop
                }
                catch { return; }
            }

            IsGated = true;
            return;
        }

        if (toolName != PlanningTool.ProposePlanName) return;

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
