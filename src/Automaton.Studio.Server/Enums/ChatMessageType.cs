namespace Automaton.Studio.Server.Enums;

/// <summary>
/// Defines the type of AI chat message
/// </summary>
public enum ChatMessageType
{
    /// <summary>
    /// Regular text message
    /// </summary>
    Text = 0,

    /// <summary>
    /// Message indicating generation in progress
    /// </summary>
    Generating = 1,

    /// <summary>
    /// Successfully generated code with apply option
    /// </summary>
    CodeSuccess = 2,

    /// <summary>
    /// Error during code generation
    /// </summary>
    CodeError = 3,

    /// <summary>
    /// System message (welcome, info, etc.)
    /// </summary>
    System = 4,

    /// <summary>
    /// MCP tool execution message
    /// </summary>
    ToolExecution = 5,

    /// <summary>
    /// AI reasoning/thinking message (collapsible)
    /// </summary>
    Reasoning = 6,

    /// <summary>
    /// Tool operation in progress
    /// </summary>
    OperationInProgress = 7,

    /// <summary>
    /// Tool operation completed
    /// </summary>
    OperationComplete = 8
}
