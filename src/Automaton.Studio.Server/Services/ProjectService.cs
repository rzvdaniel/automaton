using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing projects (organizations/workspaces)
/// </summary>
public class ProjectService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly Serilog.ILogger logger;

    private Guid UserId => userContextService.GetUserId();

    public ProjectService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        ResourceAuthorizationService authorizationService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.authorizationService = authorizationService;
        this.logger = Serilog.Log.ForContext<ProjectService>();
    }

    /// <summary>
    /// Get all projects where the current user is a member
    /// </summary>
    public async Task<IEnumerable<ProjectListItem>> ListUserProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await dbContext.ProjectMembers
            .Where(pm => pm.UserId == UserId && pm.IsActive)
            .Include(pm => pm.Project)
            .Select(pm => new ProjectListItem
            {
                Id = pm.Project.Id,
                Name = pm.Project.Name,
                DisplayName = pm.Project.DisplayName,
                Description = pm.Project.Description,
                Role = pm.Role,
                MemberCount = pm.Project.Members.Count(m => m.IsActive),
                UpdatedAt = pm.Project.Updated
            })
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(cancellationToken);

        logger.Information("Listed {Count} projects for user {UserId}", projects.Count, UserId);
        return projects;
    }

    /// <summary>
    /// Get detailed project information
    /// </summary>
    public async Task<ProjectDto?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // Check if user can access this project
        if (!await authorizationService.CanAccessProjectAsync(projectId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to access project {ProjectId} without membership", UserId, projectId);
            return null;
        }

        var project = await dbContext.Projects
            .Include(p => p.Owner)
            .Include(p => p.Members)
            .Include(p => p.ProjectApps)
            .Include(p => p.ProjectFlows)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return null;
        }

        // Get user's role in the project
        var userRole = await authorizationService.GetUserRoleInProjectAsync(projectId, UserId, cancellationToken);

        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            DisplayName = project.DisplayName,
            Description = project.Description,
            OwnerId = project.OwnerId,
            OwnerName = project.Owner?.UserName ?? "Unknown",
            CreatedAt = project.Created,
            UpdatedAt = project.Updated,
            IsActive = project.IsActive,
            MemberCount = project.Members.Count(m => m.IsActive),
            AppCount = project.ProjectApps.Count,
            FlowCount = project.ProjectFlows.Count,
            CurrentUserRole = userRole ?? ProjectRole.Viewer
        };
    }

    /// <summary>
    /// Create a new project. The creator automatically becomes the owner and ProjectAdmin.
    /// </summary>
    public async Task<ProjectDto> CreateProjectAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        // Validate unique name (URL-safe slug)
        var nameExists = await dbContext.Projects
            .AnyAsync(p => p.Name.ToLower() == request.Name.ToLower(), cancellationToken);

        if (nameExists)
        {
            throw new InvalidOperationException($"Project with name '{request.Name}' already exists");
        }

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            OwnerId = UserId,
            Created = now,
            Updated = now,
            IsActive = true
        };

        dbContext.Projects.Add(project);

        // Add creator as ProjectAdmin member
        var ownerMember = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = UserId,
            Role = ProjectRole.ProjectAdmin,
            JoinedAt = now,
            InvitedBy = null, // Owner wasn't invited
            IsActive = true
        };

        dbContext.ProjectMembers.Add(ownerMember);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Created project {ProjectId} ({ProjectName}) by user {UserId}", project.Id, project.Name, UserId);

        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            DisplayName = project.DisplayName,
            Description = project.Description,
            OwnerId = project.OwnerId,
            OwnerName = userContextService.GetUserName(),
            CreatedAt = project.Created,
            UpdatedAt = project.Updated,
            IsActive = project.IsActive,
            MemberCount = 1,
            AppCount = 0,
            FlowCount = 0,
            CurrentUserRole = ProjectRole.ProjectAdmin
        };
    }

    /// <summary>
    /// Update project details. Only ProjectAdmin can update.
    /// </summary>
    public async Task<ProjectDto?> UpdateProjectAsync(Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        // Check if user can update this project (must be ProjectAdmin)
        if (!await authorizationService.CanUpdateProjectAsync(projectId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to update project {ProjectId} without admin role", UserId, projectId);
            return null;
        }

        var project = await dbContext.Projects
            .Include(p => p.Owner)
            .Include(p => p.Members)
            .Include(p => p.ProjectApps)
            .Include(p => p.ProjectFlows)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return null;
        }

        project.DisplayName = request.DisplayName;
        project.Description = request.Description;
        project.IsActive = request.IsActive;
        project.Updated = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Updated project {ProjectId} by user {UserId}", projectId, UserId);

        // Get user's role in the project
        var userRole = await authorizationService.GetUserRoleInProjectAsync(projectId, UserId, cancellationToken);

        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            DisplayName = project.DisplayName,
            Description = project.Description,
            OwnerId = project.OwnerId,
            OwnerName = project.Owner?.UserName ?? "Unknown",
            CreatedAt = project.Created,
            UpdatedAt = project.Updated,
            IsActive = project.IsActive,
            MemberCount = project.Members.Count(m => m.IsActive),
            AppCount = project.ProjectApps.Count,
            FlowCount = project.ProjectFlows.Count,
            CurrentUserRole = userRole ?? ProjectRole.Viewer
        };
    }

    /// <summary>
    /// Delete (deactivate) a project. Only the owner can delete.
    /// </summary>
    public async Task<bool> DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // Check if user can delete this project (must be owner)
        if (!await authorizationService.CanDeleteProjectAsync(projectId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to delete project {ProjectId} without ownership", UserId, projectId);
            return false;
        }

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return false;
        }

        project.IsActive = false;
        project.Updated = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Deleted project {ProjectId} by user {UserId}", projectId, UserId);
        return true;
    }

    /// <summary>
    /// Check if user has a specific role or higher in a project
    /// </summary>
    public async Task<bool> HasRoleAsync(Guid projectId, Guid userId, ProjectRole minimumRole, CancellationToken cancellationToken = default)
    {
        return await authorizationService.HasRoleInProjectAsync(projectId, userId, minimumRole, cancellationToken);
    }

    /// <summary>
    /// Get user's membership in a project
    /// </summary>
    public async Task<ProjectMember?> GetUserProjectMembershipAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await authorizationService.GetUserProjectMembershipAsync(projectId, UserId, cancellationToken);
    }

    /// <summary>
    /// Get user's role in a project
    /// </summary>
    public async Task<ProjectRole?> GetUserRoleAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await authorizationService.GetUserRoleInProjectAsync(projectId, userId, cancellationToken);
    }
}
