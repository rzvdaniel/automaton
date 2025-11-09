using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service to manage the current project context for the authenticated user
/// </summary>
public class ProjectContextService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly Serilog.ILogger logger;
    private Guid? currentProjectId;

    public ProjectContextService(
        ApplicationDbContext dbContext,
        UserContextService userContextService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.logger = Log.ForContext<ProjectContextService>();
    }

    /// <summary>
    /// Get the current project ID for the authenticated user.
    /// If not set, returns the first project the user belongs to.
    /// Returns null if user has no projects.
    /// </summary>
    public async Task<Guid?> GetCurrentProjectIdAsync()
    {
        if (currentProjectId.HasValue)
        {
            return currentProjectId.Value;
        }

        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
        {
            logger.Warning("Cannot get current project: no authenticated user");
            return null;
        }

        // Get first project the user belongs to
        var firstProject = await dbContext.UserProjectRoles
            .Where(upr => upr.UserId == userId)
            .OrderBy(upr => upr.CreatedAt)
            .Select(upr => upr.ProjectId)
            .FirstOrDefaultAsync();

        if (firstProject != Guid.Empty)
        {
            currentProjectId = firstProject;
            logger.Debug("Auto-selected first project for user {UserId}: {ProjectId}", userId, firstProject);
        }

        return firstProject == Guid.Empty ? null : firstProject;
    }

    /// <summary>
    /// Set the current project ID for the authenticated user.
    /// Validates that the user belongs to the project before setting.
    /// </summary>
    public async Task<bool> SetCurrentProjectIdAsync(Guid projectId)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
        {
            logger.Warning("Cannot set current project: no authenticated user");
            return false;
        }

        // Validate user has access to this project
        var hasAccess = await dbContext.UserProjectRoles
            .AnyAsync(upr => upr.UserId == userId && upr.ProjectId == projectId);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to set current project to {ProjectId} but has no access",
                userId, projectId);
            return false;
        }

        currentProjectId = projectId;
        logger.Information("User {UserId} switched to project {ProjectId}", userId, projectId);
        return true;
    }

    /// <summary>
    /// Clear the current project selection
    /// </summary>
    public void ClearCurrentProject()
    {
        currentProjectId = null;
    }

    /// <summary>
    /// Check if user has access to the specified project
    /// </summary>
    public async Task<bool> HasAccessToProjectAsync(Guid projectId)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
        {
            return false;
        }

        return await dbContext.UserProjectRoles
            .AnyAsync(upr => upr.UserId == userId && upr.ProjectId == projectId);
    }

    /// <summary>
    /// Get the user's role in the specified project
    /// </summary>
    public async Task<ProjectRole?> GetUserRoleInProjectAsync(Guid projectId)
    {
        var userId = userContextService.GetUserId();
        if (userId == Guid.Empty)
        {
            return null;
        }

        var role = await dbContext.UserProjectRoles
            .Where(upr => upr.UserId == userId && upr.ProjectId == projectId)
            .Select(upr => upr.Role)
            .FirstOrDefaultAsync();

        return role;
    }
}
