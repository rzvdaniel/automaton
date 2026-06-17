namespace Automaton.Api.Enums;

/// <summary>
/// Defines the roles that can be assigned to project members.
/// </summary>
public enum ProjectRole
{
    /// <summary>
    /// Full control over the project including managing members and settings.
    /// </summary>
    ProjectAdmin = 0,
    
    /// <summary>
    /// Can create and edit apps, flows, steps, and runners. Full development access.
    /// </summary>
    Developer = 1,
    
    /// <summary>
    /// Can only view and execute apps. Consumer-focused role.
    /// </summary>
    AppUser = 2,
    
    /// <summary>
    /// Read-only access to project resources. Can view but not execute or edit.
    /// </summary>
    Viewer = 3
}
