namespace Automaton.Studio.Server.Enums;

/// <summary>
/// Defines the type of resource that a chat session is associated with
/// </summary>
public enum ChatResourceType
{
    /// <summary>
    /// General chat session (not associated with any specific resource)
    /// </summary>
    General = 0,

    /// <summary>
    /// Chat session associated with a Custom Step
    /// </summary>
    CustomStep = 1,

    /// <summary>
    /// Chat session associated with a Web App
    /// </summary>
    WebApp = 2
}
