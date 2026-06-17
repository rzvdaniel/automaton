using Microsoft.AspNetCore.Authorization;
using Automaton.Api.Enums;

namespace Automaton.Api.Authorization;

/// <summary>
/// Authorization requirement for project membership with specific roles
/// </summary>
public class ProjectMemberRequirement : IAuthorizationRequirement
{
    public ProjectRole[] AllowedRoles { get; }

    public ProjectMemberRequirement(params ProjectRole[] allowedRoles)
    {
        AllowedRoles = allowedRoles ?? throw new ArgumentNullException(nameof(allowedRoles));
        
        if (allowedRoles.Length == 0)
        {
            throw new ArgumentException("At least one role must be specified", nameof(allowedRoles));
        }
    }
}

/// <summary>
/// Authorization requirement to ensure user is a project admin
/// </summary>
public class ProjectAdminRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Authorization requirement to ensure user can manage resources (ProjectAdmin or Developer)
/// </summary>
public class ProjectResourceManagerRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Authorization requirement to ensure user can execute apps (all roles except Viewer)
/// </summary>
public class ProjectAppExecutorRequirement : IAuthorizationRequirement
{
}
