namespace Automaton.Studio.Server.Enums;

/// <summary>
/// Defines the roles that can be assigned to organization members.
/// </summary>
public enum OrganizationRole
{
    /// <summary>
    /// Full control over the organization including managing members, settings, and all projects.
    /// Can create projects, invite members, and assign apps to users.
    /// </summary>
    OrgAdmin = 0,
    
    /// <summary>
    /// Can create and manage projects within the organization. Full development access to own projects.
    /// </summary>
    Developer = 1,
    
    /// <summary>
    /// Can only access apps that have been explicitly assigned to them. Consumer-focused role.
    /// Simplified UI with no development tools visible.
    /// </summary>
    AppUser = 2
}
