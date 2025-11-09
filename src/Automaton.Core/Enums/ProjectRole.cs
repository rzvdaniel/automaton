namespace Automaton.Core.Enums;

/// <summary>
/// Defines the role a user has within a project/organization
/// </summary>
public enum ProjectRole
{
    /// <summary>
    /// Can view project resources (read-only access)
    /// </summary>
    Viewer = 1,
    
    /// <summary>
    /// Can create and manage flows, apps, and steps (includes Viewer permissions)
    /// </summary>
    Creator = 2,
    
    /// <summary>
    /// Full control over the project including user management (includes all permissions)
    /// </summary>
    Owner = 3
}
