using Automaton.Core.Enums;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services;

public class ProjectService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly RecentActivityService recentActivityService;
    private readonly Serilog.ILogger logger;

    private Guid UserId => userContextService.GetUserId();

    public ProjectService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        RecentActivityService recentActivityService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.recentActivityService = recentActivityService;
        this.logger = Serilog.Log.ForContext<ProjectService>();
    }

    /// <summary>
    /// List all projects the current user belongs to
    /// </summary>
    public async Task<IEnumerable<ProjectModel>> ListAsync()
    {
        var projects = await dbContext.Projects
            .Where(p => p.UserProjectRoles.Any(upr => upr.UserId == UserId))
            .Select(p => new ProjectModel
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                AdminUserId = p.AdminUserId,
                AdminUserName = p.AdminUser.UserName,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                MemberCount = p.UserProjectRoles.Count()
            })
            .OrderBy(p => p.Name)
            .ToListAsync();

        return projects;
    }

    /// <summary>
    /// Get a specific project by ID (user must be a member)
    /// </summary>
    public async Task<ProjectModel?> GetAsync(Guid projectId)
    {
        var project = await dbContext.Projects
            .Where(p => p.Id == projectId && p.UserProjectRoles.Any(upr => upr.UserId == UserId))
            .Select(p => new ProjectModel
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                AdminUserId = p.AdminUserId,
                AdminUserName = p.AdminUser.UserName,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                MemberCount = p.UserProjectRoles.Count()
            })
            .FirstOrDefaultAsync();

        return project;
    }

    /// <summary>
    /// Create a new project (user becomes admin/owner)
    /// </summary>
    public async Task<Guid> CreateAsync(ProjectCreateRequest request)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            AdminUserId = UserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Projects.Add(project);

        // Add creator as owner
        var userProjectRole = new UserProjectRole
        {
            UserId = UserId,
            ProjectId = project.Id,
            Role = ProjectRole.Owner,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.UserProjectRoles.Add(userProjectRole);

        await dbContext.SaveChangesAsync();

        await recentActivityService.LogActivityAsync(
            ActivityType.ProjectCreated,
            project.Name,
            project.Id);

        logger.Information("User {UserId} created project {ProjectId} '{ProjectName}'",
            UserId, project.Id, project.Name);

        return project.Id;
    }

    /// <summary>
    /// Update a project (requires Owner role)
    /// </summary>
    public async Task<bool> UpdateAsync(Guid projectId, ProjectUpdateRequest request)
    {
        var project = await dbContext.Projects
            .Include(p => p.UserProjectRoles)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            return false;
        }

        // Check user is owner
        var userRole = project.UserProjectRoles.FirstOrDefault(upr => upr.UserId == UserId);
        if (userRole == null || userRole.Role != ProjectRole.Owner)
        {
            logger.Warning("User {UserId} attempted to update project {ProjectId} without Owner role",
                UserId, projectId);
            return false;
        }

        project.Name = request.Name;
        project.Description = request.Description;
        project.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        await recentActivityService.LogActivityAsync(
            ActivityType.ProjectUpdated,
            project.Name,
            projectId);

        logger.Information("User {UserId} updated project {ProjectId}", UserId, projectId);

        return true;
    }

    /// <summary>
    /// Delete a project (requires Owner role and must be admin)
    /// </summary>
    public async Task<bool> DeleteAsync(Guid projectId)
    {
        var project = await dbContext.Projects
            .Include(p => p.UserProjectRoles)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            return false;
        }

        // Only admin can delete
        if (project.AdminUserId != UserId)
        {
            logger.Warning("User {UserId} attempted to delete project {ProjectId} but is not admin",
                UserId, projectId);
            return false;
        }

        dbContext.Projects.Remove(project);
        await dbContext.SaveChangesAsync();

        await recentActivityService.LogActivityAsync(
            ActivityType.ProjectDeleted,
            project.Name,
            projectId);

        logger.Information("User {UserId} deleted project {ProjectId}", UserId, projectId);

        return true;
    }

    /// <summary>
    /// List all members of a project
    /// </summary>
    public async Task<IEnumerable<ProjectMemberModel>> ListMembersAsync(Guid projectId)
    {
        // Check user has access to project
        var hasAccess = await dbContext.UserProjectRoles
            .AnyAsync(upr => upr.UserId == UserId && upr.ProjectId == projectId);

        if (!hasAccess)
        {
            return Enumerable.Empty<ProjectMemberModel>();
        }

        var members = await dbContext.UserProjectRoles
            .Where(upr => upr.ProjectId == projectId)
            .Select(upr => new ProjectMemberModel
            {
                UserId = upr.UserId,
                UserName = upr.User.UserName,
                Email = upr.User.Email,
                Role = upr.Role,
                AddedAt = upr.CreatedAt
            })
            .OrderBy(m => m.UserName)
            .ToListAsync();

        return members;
    }

    /// <summary>
    /// Add a member to a project (requires Owner role)
    /// </summary>
    public async Task<bool> AddMemberAsync(Guid projectId, AddProjectMemberRequest request)
    {
        var project = await dbContext.Projects
            .Include(p => p.UserProjectRoles)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            return false;
        }

        // Check current user is owner
        var currentUserRole = project.UserProjectRoles.FirstOrDefault(upr => upr.UserId == UserId);
        if (currentUserRole == null || currentUserRole.Role != ProjectRole.Owner)
        {
            logger.Warning("User {UserId} attempted to add member to project {ProjectId} without Owner role",
                UserId, projectId);
            return false;
        }

        // Check if user is already a member
        if (project.UserProjectRoles.Any(upr => upr.UserId == request.UserId))
        {
            logger.Warning("User {UserId} is already a member of project {ProjectId}",
                request.UserId, projectId);
            return false;
        }

        var userProjectRole = new UserProjectRole
        {
            UserId = request.UserId,
            ProjectId = projectId,
            Role = request.Role,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.UserProjectRoles.Add(userProjectRole);
        await dbContext.SaveChangesAsync();

        logger.Information("User {UserId} added user {NewUserId} to project {ProjectId} with role {Role}",
            UserId, request.UserId, projectId, request.Role);

        return true;
    }

    /// <summary>
    /// Update a member's role in a project (requires Owner role)
    /// </summary>
    public async Task<bool> UpdateMemberRoleAsync(Guid projectId, Guid memberId, UpdateProjectMemberRequest request)
    {
        var project = await dbContext.Projects
            .Include(p => p.UserProjectRoles)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            return false;
        }

        // Check current user is owner
        var currentUserRole = project.UserProjectRoles.FirstOrDefault(upr => upr.UserId == UserId);
        if (currentUserRole == null || currentUserRole.Role != ProjectRole.Owner)
        {
            logger.Warning("User {UserId} attempted to update member role in project {ProjectId} without Owner role",
                UserId, projectId);
            return false;
        }

        var memberRole = project.UserProjectRoles.FirstOrDefault(upr => upr.UserId == memberId);
        if (memberRole == null)
        {
            return false;
        }

        // Don't allow admin to change their own role
        if (memberId == project.AdminUserId)
        {
            logger.Warning("Cannot change role of project admin {UserId} in project {ProjectId}",
                memberId, projectId);
            return false;
        }

        memberRole.Role = request.Role;
        await dbContext.SaveChangesAsync();

        logger.Information("User {UserId} updated role of user {MemberId} in project {ProjectId} to {Role}",
            UserId, memberId, projectId, request.Role);

        return true;
    }

    /// <summary>
    /// Remove a member from a project (requires Owner role)
    /// </summary>
    public async Task<bool> RemoveMemberAsync(Guid projectId, Guid memberId)
    {
        var project = await dbContext.Projects
            .Include(p => p.UserProjectRoles)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            return false;
        }

        // Check current user is owner
        var currentUserRole = project.UserProjectRoles.FirstOrDefault(upr => upr.UserId == UserId);
        if (currentUserRole == null || currentUserRole.Role != ProjectRole.Owner)
        {
            logger.Warning("User {UserId} attempted to remove member from project {ProjectId} without Owner role",
                UserId, projectId);
            return false;
        }

        // Don't allow removing the admin
        if (memberId == project.AdminUserId)
        {
            logger.Warning("Cannot remove project admin {UserId} from project {ProjectId}",
                memberId, projectId);
            return false;
        }

        var memberRole = project.UserProjectRoles.FirstOrDefault(upr => upr.UserId == memberId);
        if (memberRole == null)
        {
            return false;
        }

        dbContext.UserProjectRoles.Remove(memberRole);
        await dbContext.SaveChangesAsync();

        logger.Information("User {UserId} removed user {MemberId} from project {ProjectId}",
            UserId, memberId, projectId);

        return true;
    }

    /// <summary>
    /// Create a default project for a new user (used during registration)
    /// </summary>
    public async Task<Guid> CreateDefaultProjectForUserAsync(Guid userId, string userName)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = $"{userName}'s Project",
            Description = "Default project",
            AdminUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Projects.Add(project);

        // Add user as owner
        var userProjectRole = new UserProjectRole
        {
            UserId = userId,
            ProjectId = project.Id,
            Role = ProjectRole.Owner,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.UserProjectRoles.Add(userProjectRole);

        await dbContext.SaveChangesAsync();

        logger.Information("Created default project {ProjectId} '{ProjectName}' for new user {UserId}",
            project.Id, project.Name, userId);

        return project.Id;
    }
}
