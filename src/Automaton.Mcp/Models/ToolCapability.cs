namespace Automaton.Mcp.Models;

/// <summary>
/// Declares what category a tool belongs to.
/// Conversation modes use this to decide which tools are available — no hardcoded name lists.
/// </summary>
public enum ToolCapability
{
    /// <summary>Safe to call in any mode — reads files, lists, searches.</summary>
    Read,

    /// <summary>Modifies the file system — write, edit, delete, rename, instructions.</summary>
    Write,

    /// <summary>Plan lifecycle tools — propose_plan, stage_complete. Available in Plan mode only.</summary>
    Planning,

    /// <summary>Mode-switching tools — request_plan. Available in Agent mode only.</summary>
    ModeSwitch,

    /// <summary>
    /// Complexity assessment tool — assess_task. Available in Agent mode only.
    /// Called as the mandatory first action to reason about task complexity before any read/write.
    /// </summary>
    Assessment,
}
