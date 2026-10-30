using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Automaton.Api.Enums;
using Automaton.Api.Data;

namespace Automaton.Api.Authorization;

internal static class ProjectAuthorizationIdentifiers
{
    public static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var userIdValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdValue, out userId);
    }

    public static bool TryGetProjectId(HttpContext? httpContext, out Guid projectId)
    {
        var projectIdValue = httpContext?.Request.RouteValues["projectId"]?.ToString()
            ?? httpContext?.Request.RouteValues["id"]?.ToString();
        return Guid.TryParse(projectIdValue, out projectId);
    }
}

/// <summary>
/// Handles authorization for project membership requirements
/// </summary>
public class ProjectMemberHandler : AuthorizationHandler<ProjectMemberRequirement>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProjectMemberHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProjectMemberHandler(
        ApplicationDbContext context,
        ILogger<ProjectMemberHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProjectMemberRequirement requirement)
    {
        // Get user ID from claims
        if (!ProjectAuthorizationIdentifiers.TryGetUserId(context.User, out var userId))
        {
            _logger.LogWarning("Authorization failed: User ID not found in claims or invalid format");
            return;
        }

        // Get project ID from route data
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogWarning("Authorization failed: HTTP context not available");
            return;
        }

        if (!ProjectAuthorizationIdentifiers.TryGetProjectId(httpContext, out var projectId))
        {
            _logger.LogWarning("Authorization failed: Project ID not found in route or invalid format");
            return;
        }

        // Check if user is a member of the project with one of the allowed roles
        var membership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => 
                pm.ProjectId == projectId 
                && pm.UserId == userId 
                && pm.IsActive);

        if (membership == null)
        {
            _logger.LogWarning("Authorization failed: User {UserId} is not a member of project {ProjectId}", 
                userId, projectId);
            return;
        }

        // Check if user has one of the allowed roles
        if (requirement.AllowedRoles.Contains(membership.Role))
        {
            _logger.LogDebug("Authorization succeeded: User {UserId} has role {Role} in project {ProjectId}",
                userId, membership.Role, projectId);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("Authorization failed: User {UserId} has role {UserRole} but requires one of [{AllowedRoles}]", 
                userId, membership.Role, string.Join(", ", requirement.AllowedRoles));
        }
    }
}

/// <summary>
/// Handles authorization for project admin requirements
/// </summary>
public class ProjectAdminHandler : AuthorizationHandler<ProjectAdminRequirement>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProjectAdminHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProjectAdminHandler(
        ApplicationDbContext context,
        ILogger<ProjectAdminHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProjectAdminRequirement requirement)
    {
        if (!ProjectAuthorizationIdentifiers.TryGetUserId(context.User, out var userId))
        {
            return;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return;
        }

        if (!ProjectAuthorizationIdentifiers.TryGetProjectId(httpContext, out var projectId))
        {
            return;
        }

        var membership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => 
                pm.ProjectId == projectId 
                && pm.UserId == userId 
                && pm.IsActive
                && pm.Role == ProjectRole.ProjectAdmin);

        if (membership != null)
        {
            _logger.LogDebug("Authorization succeeded: User {UserId} is ProjectAdmin of project {ProjectId}",
                userId, projectId);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("Authorization failed: User {UserId} is not ProjectAdmin of project {ProjectId}", 
                userId, projectId);
        }
    }
}

/// <summary>
/// Handles authorization for resource management (ProjectAdmin or Developer)
/// </summary>
public class ProjectResourceManagerHandler : AuthorizationHandler<ProjectResourceManagerRequirement>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProjectResourceManagerHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProjectResourceManagerHandler(
        ApplicationDbContext context,
        ILogger<ProjectResourceManagerHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProjectResourceManagerRequirement requirement)
    {
        if (!ProjectAuthorizationIdentifiers.TryGetUserId(context.User, out var userId))
        {
            return;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return;
        }

        if (!ProjectAuthorizationIdentifiers.TryGetProjectId(httpContext, out var projectId))
        {
            return;
        }

        // Check if user is ProjectAdmin or Developer
        var membership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => 
                pm.ProjectId == projectId 
                && pm.UserId == userId 
                && pm.IsActive
                && (pm.Role == ProjectRole.ProjectAdmin || pm.Role == ProjectRole.Developer));

        if (membership != null)
        {
            _logger.LogDebug("Authorization succeeded: User {UserId} can manage resources in project {ProjectId} (Role: {Role})",
                userId, projectId, membership.Role);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("Authorization failed: User {UserId} cannot manage resources in project {ProjectId}", 
                userId, projectId);
        }
    }
}

/// <summary>
/// Handles authorization for app execution (all roles except Viewer)
/// </summary>
public class ProjectAppExecutorHandler : AuthorizationHandler<ProjectAppExecutorRequirement>
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProjectAppExecutorHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ProjectAppExecutorHandler(
        ApplicationDbContext context,
        ILogger<ProjectAppExecutorHandler> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProjectAppExecutorRequirement requirement)
    {
        if (!ProjectAuthorizationIdentifiers.TryGetUserId(context.User, out var userId))
        {
            return;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return;
        }

        if (!ProjectAuthorizationIdentifiers.TryGetProjectId(httpContext, out var projectId))
        {
            return;
        }

        // Check if user is ProjectAdmin, Developer, or AppUser (NOT Viewer)
        var membership = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => 
                pm.ProjectId == projectId 
                && pm.UserId == userId 
                && pm.IsActive
                && pm.Role != ProjectRole.Viewer);

        if (membership != null)
        {
            _logger.LogDebug("Authorization succeeded: User {UserId} can execute apps in project {ProjectId} (Role: {Role})",
                userId, projectId, membership.Role);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("Authorization failed: User {UserId} cannot execute apps in project {ProjectId}", 
                userId, projectId);
        }
    }
}
