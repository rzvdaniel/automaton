using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Api.Services;

/// <summary>
/// Service for managing projects (organizations/workspaces)
/// </summary>
public class ProjectService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly ResourceAuthorizationService authorizationService;
    private readonly IAuditEventService auditEventService;
    private readonly Serilog.ILogger logger;

    private Guid UserId => userContextService.GetUserId();

    public ProjectService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        ResourceAuthorizationService authorizationService,
        IAuditEventService auditEventService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.authorizationService = authorizationService;
        this.auditEventService = auditEventService;
        this.logger = Serilog.Log.ForContext<ProjectService>();
    }

    /// <summary>
    /// Get all projects where the current user is a member
    /// </summary>
    public async Task<IEnumerable<ProjectListItem>> ListUserProjectsAsync(CancellationToken cancellationToken = default)
    {
        var projects = await (
            from membership in dbContext.ProjectMembers
            join project in dbContext.Projects on membership.ProjectId equals project.Id
            where membership.UserId == UserId && membership.IsActive
            select new ProjectListItem
            {
                Id = project.Id,
                Name = project.Name,
                DisplayName = project.DisplayName,
                Description = project.Description,
                Role = membership.Role,
                MemberCount = dbContext.ProjectMembers.Count(member => member.ProjectId == project.Id && member.IsActive),
                UpdatedAt = project.Updated,
                OrganizationId = project.OrganizationId,
                OrganizationName = project.Organization != null ? project.Organization.Name : null,
                IsActive = project.IsActive
            })
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(cancellationToken);

        logger.Debug("Listed {Count} projects for user {UserId}", projects.Count, UserId);
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
            .Include(p => p.Organization)
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
            OrganizationId = project.OrganizationId,
            OrganizationName = project.Organization?.Name,
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
        
        // If organizationId is provided, verify user is a member
        if (request.OrganizationId.HasValue)
        {
            var isMember = await dbContext.OrganizationMembers
                .AnyAsync(om => om.OrganizationId == request.OrganizationId.Value && om.UserId == UserId && om.IsActive, cancellationToken);
                
            if (!isMember)
            {
                throw new UnauthorizedAccessException("You must be a member of the organization to create projects under it");
            }
        }

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            OwnerId = UserId,
            OrganizationId = request.OrganizationId,
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

        await auditEventService.WriteAsync(
            action: "Created",
            entityType: "Project",
            entityId: project.Id,
            message: $"Project '{project.DisplayName}' was created",
            metadata: new
            {
                project.Name,
                project.DisplayName,
                project.OrganizationId,
                UserId
            },
            cancellationToken: cancellationToken);


        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            DisplayName = project.DisplayName,
            Description = project.Description,
            OwnerId = project.OwnerId,
            OwnerName = userContextService.GetUserName(),
            OrganizationId = project.OrganizationId,
            OrganizationName = null, // Will be loaded if needed
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

        await auditEventService.WriteAsync(
            action: "Updated",
            entityType: "Project",
            entityId: projectId,
            message: $"Project '{project.DisplayName}' was updated",
            metadata: new
            {
                project.Name,
                project.DisplayName,
                project.Description,
                project.IsActive,
                UserId
            },
            cancellationToken: cancellationToken);


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

        await auditEventService.WriteAsync(
            action: "Deleted",
            entityType: "Project",
            entityId: projectId,
            message: $"Project '{project.DisplayName}' was deleted",
            metadata: new
            {
                project.Name,
                project.DisplayName,
                UserId
            },
            cancellationToken: cancellationToken);

        return true;
    }



    /// <summary>
    /// Reactivate a previously deleted (deactivated) project. Only the owner can reactivate.
    /// </summary>
    public async Task<bool> ReactivateProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // Check if user can reactivate this project (must be owner)
        if (!await authorizationService.CanDeleteProjectAsync(projectId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to reactivate project {ProjectId} without ownership", UserId, projectId);
            return false;
        }

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return false;
        }

        project.IsActive = true;
        project.Updated = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditEventService.WriteAsync(
            action: "Reactivated",
            entityType: "Project",
            entityId: projectId,
            message: $"Project '{project.DisplayName}' was reactivated",
            metadata: new
            {
                project.Name,
                project.DisplayName,
                UserId
            },
            cancellationToken: cancellationToken);

        return true;
    }

    /// <summary>
    /// Get user's role in a project
    /// </summary>
    public async Task<ProjectRole?> GetUserRoleAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await authorizationService.GetUserRoleInProjectAsync(projectId, userId, cancellationToken);
    }
}
