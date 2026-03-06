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

    /// <summary>Plan lifecycle tools — propose_plan, stage_complete.</summary>
    Planning,
}
