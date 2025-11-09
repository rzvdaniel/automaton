namespace Automaton.Core.Enums;

/// <summary>
/// Defines the publishing scope for web applications
/// </summary>
public enum PublishingScope
{
    /// <summary>
    /// App is accessible to anyone (no authentication required)
    /// </summary>
    Anonymous = 1,
    
    /// <summary>
    /// App is accessible only to members of the owning project
    /// </summary>
    ProjectMember = 2
}
