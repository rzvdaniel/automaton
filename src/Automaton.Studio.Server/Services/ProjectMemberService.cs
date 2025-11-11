using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing project members
/// </summary>
public class ProjectMemberService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly EmailService emailService;
    private readonly Serilog.ILogger logger;

    private Guid UserId => userContextService.GetUserId();

    public ProjectMemberService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        UserManager<ApplicationUser> userManager,
        EmailService emailService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.userManager = userManager;
        this.emailService = emailService;
        this.logger = Serilog.Log.ForContext<ProjectMemberService>();
    }

    /// <summary>
    /// Get all active members of a project
    /// </summary>
    public async Task<IEnumerable<ProjectMemberDto>> ListProjectMembersAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        // Check if current user has access to this project
        var hasAccess = await dbContext.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to list members of project {ProjectId} without access", UserId, projectId);
            return Enumerable.Empty<ProjectMemberDto>();
        }

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        var members = await dbContext.ProjectMembers
            .Where(pm => pm.ProjectId == projectId && pm.IsActive)
            .Include(pm => pm.User)
            .Include(pm => pm.Inviter)
            .Select(pm => new ProjectMemberDto
            {
                ProjectId = pm.ProjectId,
                UserId = pm.UserId,
                UserName = pm.User.UserName ?? string.Empty,
                Email = pm.User.Email ?? string.Empty,
                Role = pm.Role,
                JoinedAt = pm.JoinedAt,
                InvitedBy = pm.InvitedBy,
                InvitedByName = pm.Inviter != null ? pm.Inviter.UserName : null,
                IsActive = pm.IsActive,
                IsOwner = project != null && pm.UserId == project.OwnerId
            })
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);

        logger.Information("Listed {Count} members for project {ProjectId}", members.Count, projectId);
        return members;
    }

    /// <summary>
    /// Add a member to a project. Creates account if it doesn't exist.
    /// Only ProjectAdmin can add members.
    /// </summary>
    public async Task<ProjectMemberDto?> AddMemberAsync(Guid projectId, AddProjectMemberRequest request, CancellationToken cancellationToken = default)
    {
        // Check if current user is ProjectAdmin
        var currentMembership = await dbContext.ProjectMembers
            .Include(pm => pm.Project)
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (currentMembership == null || currentMembership.Role != ProjectRole.ProjectAdmin)
        {
            logger.Warning("User {UserId} attempted to add member to project {ProjectId} without admin role", UserId, projectId);
            return null;
        }

        // Find or create user
        var user = await userManager.FindByEmailAsync(request.Email);
        
        if (user == null)
        {
            if (!request.CreateAccountIfNotExists)
            {
                throw new InvalidOperationException($"User with email '{request.Email}' does not exist and account creation is disabled");
            }

            // Create new user account
            user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                EmailConfirmed = false,
                IsEnterpriseEnabled = true // Enable enterprise features for invited users
            };

            var result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to create user account: {errors}");
            }

            logger.Information("Created new user {UserId} ({Email}) for project invitation", user.Id, user.Email);

            // TODO: Send invitation email with password setup link
            // await emailService.SendProjectInvitationAsync(user.Email, currentMembership.Project.DisplayName);
        }

        // Check if user is already a member
        var existingMembership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == user.Id, cancellationToken);

        if (existingMembership != null)
        {
            if (existingMembership.IsActive)
            {
                throw new InvalidOperationException($"User '{request.Email}' is already a member of this project");
            }

            // Reactivate membership
            existingMembership.IsActive = true;
            existingMembership.Role = request.Role;
            existingMembership.InvitedBy = UserId;
            existingMembership.JoinedAt = DateTime.UtcNow;
        }
        else
        {
            // Create new membership
            var membership = new ProjectMember
            {
                ProjectId = projectId,
                UserId = user.Id,
                Role = request.Role,
                JoinedAt = DateTime.UtcNow,
                InvitedBy = UserId,
                IsActive = true
            };

            dbContext.ProjectMembers.Add(membership);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Added user {UserId} ({Email}) to project {ProjectId} with role {Role}", 
            user.Id, user.Email, projectId, request.Role);

        return new ProjectMemberDto
        {
            ProjectId = projectId,
            UserId = user.Id,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Role = request.Role,
            JoinedAt = DateTime.UtcNow,
            InvitedBy = UserId,
            InvitedByName = userContextService.GetUserName(),
            IsActive = true,
            IsOwner = false
        };
    }

    /// <summary>
    /// Update a member's role. Only ProjectAdmin can update roles.
    /// Cannot change owner's role.
    /// </summary>
    public async Task<ProjectMemberDto?> UpdateMemberRoleAsync(Guid projectId, Guid userId, UpdateProjectMemberRequest request, CancellationToken cancellationToken = default)
    {
        // Check if current user is ProjectAdmin
        var currentMembership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (currentMembership == null || currentMembership.Role != ProjectRole.ProjectAdmin)
        {
            logger.Warning("User {UserId} attempted to update member role in project {ProjectId} without admin role", UserId, projectId);
            return null;
        }

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return null;
        }

        // Cannot change owner's role
        if (userId == project.OwnerId)
        {
            throw new InvalidOperationException("Cannot change the project owner's role");
        }

        var membership = await dbContext.ProjectMembers
            .Include(pm => pm.User)
            .Include(pm => pm.Inviter)
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.IsActive, cancellationToken);

        if (membership == null)
        {
            return null;
        }

        membership.Role = request.Role;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Updated user {UserId} role to {Role} in project {ProjectId}", userId, request.Role, projectId);

        return new ProjectMemberDto
        {
            ProjectId = membership.ProjectId,
            UserId = membership.UserId,
            UserName = membership.User.UserName ?? string.Empty,
            Email = membership.User.Email ?? string.Empty,
            Role = membership.Role,
            JoinedAt = membership.JoinedAt,
            InvitedBy = membership.InvitedBy,
            InvitedByName = membership.Inviter?.UserName,
            IsActive = membership.IsActive,
            IsOwner = false
        };
    }

    /// <summary>
    /// Remove a member from a project. Only ProjectAdmin can remove members.
    /// Cannot remove the project owner.
    /// </summary>
    public async Task<bool> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check if current user is ProjectAdmin
        var currentMembership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (currentMembership == null || currentMembership.Role != ProjectRole.ProjectAdmin)
        {
            logger.Warning("User {UserId} attempted to remove member from project {ProjectId} without admin role", UserId, projectId);
            return false;
        }

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project == null)
        {
            return false;
        }

        // Cannot remove owner
        if (userId == project.OwnerId)
        {
            throw new InvalidOperationException("Cannot remove the project owner from the project");
        }

        var membership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.IsActive, cancellationToken);

        if (membership == null)
        {
            return false;
        }

        // Soft delete - deactivate membership
        membership.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("Removed user {UserId} from project {ProjectId}", userId, projectId);
        return true;
    }

    /// <summary>
    /// Get a specific member of a project
    /// </summary>
    public async Task<ProjectMemberDto?> GetMemberAsync(Guid projectId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Check if current user has access to this project
        var hasAccess = await dbContext.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == UserId && pm.IsActive, cancellationToken);

        if (!hasAccess)
        {
            logger.Warning("User {UserId} attempted to get member details in project {ProjectId} without access", UserId, projectId);
            return null;
        }

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        var membership = await dbContext.ProjectMembers
            .Include(pm => pm.User)
            .Include(pm => pm.Inviter)
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == userId && pm.IsActive, cancellationToken);

        if (membership == null)
        {
            return null;
        }

        return new ProjectMemberDto
        {
            ProjectId = membership.ProjectId,
            UserId = membership.UserId,
            UserName = membership.User.UserName ?? string.Empty,
            Email = membership.User.Email ?? string.Empty,
            Role = membership.Role,
            JoinedAt = membership.JoinedAt,
            InvitedBy = membership.InvitedBy,
            InvitedByName = membership.Inviter?.UserName,
            IsActive = membership.IsActive,
            IsOwner = project != null && membership.UserId == project.OwnerId
        };
    }
}
