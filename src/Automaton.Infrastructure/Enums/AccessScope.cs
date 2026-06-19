namespace Automaton.Api.Enums;

/// <summary>
/// Defines the access scope for resources (Apps, Flows, etc.)
/// </summary>
public enum AccessScope
{
    /// <summary>
    /// Resource is owned by a specific user (backward compatible with existing behavior).
    /// </summary>
    Personal = 0,

    /// <summary>
    /// Resource belongs to a project and is accessible by project members based on their role.
    /// </summary>
    Project = 1,

    /// <summary>
    /// Resource is published publicly and accessible to anyone.
    /// </summary>
    Public = 2
}
