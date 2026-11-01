using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text;

namespace Automaton.Api.Services;

/// <summary>
/// Service for managing project members
/// </summary>
public class ProjectMemberService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly IEmailService emailService;
    private readonly Serilog.ILogger logger;
    private readonly InvitationTokenService invitationService;
    private readonly ConfigurationService configurationService;
    private readonly UsersService usersService;
    private readonly IAuditEventService auditEventService;

    private Guid UserId => userContextService.GetUserId();

    public ProjectMemberService(
        ApplicationDbContext dbContext,
        UserContextService userContextService,
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        InvitationTokenService invitationService,
        ConfigurationService configurationService,
        UsersService usersService,
        IAuditEventService auditEventService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.userManager = userManager;
        this.emailService = emailService;
        this.invitationService = invitationService;
        this.configurationService = configurationService;
        this.usersService = usersService;
        this.auditEventService = auditEventService;
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

        var memberRecords = await dbContext.ProjectMembers
            .Where(pm => pm.ProjectId == projectId && pm.IsActive)
            .Include(pm => pm.User)
            .Include(pm => pm.Inviter)
            .OrderBy(pm => pm.JoinedAt)
            .ToListAsync(cancellationToken);
        var members = memberRecords
            .Select(member => MapMemberToDto(member, project?.OwnerId))
            .ToList();

        logger.Debug("Listed {Count} members for project {ProjectId}", members.Count, projectId);
        return members;
    }

    /// <summary>
    /// Add an existing user to a project. Only ProjectAdmin can add members; unknown emails are refused.
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

        // Project admins add people who already have an account. New accounts come only from a server
        // administrator (Admin -> Users -> Add user), because public sign-up is disabled on purpose.
        var user = await userManager.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);
        var project = currentMembership.Project;
        if (user == null || user.IsDeleted)
            throw new ProjectMemberAccountMissingException(request.Email ?? string.Empty);
        const bool createdNewUser = false;

        await SendInvitationEmailAsync(projectId, project, user, request, createdNewUser, cancellationToken);

        return await SaveMembershipAndAuditAsync(projectId, user, request, createdNewUser, cancellationToken);
    }

    private async Task<ProjectMemberDto> SaveMembershipAndAuditAsync(
        Guid projectId,
        ApplicationUser user,
        AddProjectMemberRequest request,
        bool createdNewUser,
        CancellationToken cancellationToken)
    {
        var existingMembership = await dbContext.ProjectMembers
            .FirstOrDefaultAsync(pm => pm.ProjectId == projectId && pm.UserId == user.Id, cancellationToken);
        if (existingMembership?.IsActive == true)
            throw new InvalidOperationException($"User '{request.Email}' is already a member of this project");

        if (existingMembership != null)
        {
            existingMembership.IsActive = true;
            existingMembership.Role = request.Role;
            existingMembership.InvitedBy = UserId;
            existingMembership.JoinedAt = DateTime.UtcNow;
        }
        else
        {
            dbContext.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                UserId = user.Id,
                Role = request.Role,
                JoinedAt = DateTime.UtcNow,
                InvitedBy = UserId,
                IsActive = true
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditEventService.WriteAsync(
            action: createdNewUser ? "Invited" : "AddedMember",
            entityType: "ProjectMember",
            entityId: user.Id,
            message: $"User '{user.Email}' was added to project",
            metadata: new
            {
                ProjectId = projectId,
                MemberUserId = user.Id,
                user.Email,
                request.Role,
                CreatedNewUser = createdNewUser,
                UserId
            },
            cancellationToken: cancellationToken);

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

    private async Task SendInvitationEmailAsync(
        Guid projectId,
        Project? project,
        ApplicationUser user,
        AddProjectMemberRequest request,
        bool createdNewUser,
        CancellationToken cancellationToken)
    {
        try
        {
            if (invitationService == null || project == null)
                return;

            var token = await invitationService.GenerateInvitationToken(
                projectId, request.Email, request.Role.ToString(), UserId);
            var invitationUrl = $"{configurationService.WebsiteUrl.TrimEnd('/')}/projects/invite/{token}";
            var requirePasswordSetup = createdNewUser || !(await userManager.HasPasswordAsync(user));
            var setupPasswordUrl = requirePasswordSetup
                ? await CreatePasswordSetupUrlAsync(user)
                : null;
            var emailDetails = CreateInvitationEmail(project, user, request, invitationUrl,
                requirePasswordSetup, setupPasswordUrl);
            await emailService.Send(emailDetails, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to generate or send invitation email to {Email}", request.Email);
        }
    }

    // Studio's /reset-password page sets the password; /account/createpassword never existed.
    private Task<string> CreatePasswordSetupUrlAsync(ApplicationUser user)
        => AdminUserProvisioningService.CreatePasswordSetupUrlAsync(userManager, user, configurationService.WebsiteUrl);

    private EmailData CreateInvitationEmail(
        Project project,
        ApplicationUser user,
        AddProjectMemberRequest request,
        string invitationUrl,
        bool requirePasswordSetup,
        string? setupPasswordUrl)
    {
        var variables = new Dictionary<string, string>
        {
            { "Name", user.Name ?? request.Email },
            { "ProjectName", project.DisplayName },
            { "ProjectDescription", project.Description ?? string.Empty },
            { "InviterName", userContextService.GetUserName() ?? string.Empty },
            { "Role", request.Role.ToString() },
            { "InviteUrl", invitationUrl },
            { "ExpirationHours", "24" },
            { "SupportEmail", configurationService.EmailConfig.SupportEmail },
            { "RequirePasswordSetup", requirePasswordSetup ? "true" : "false" }
        };

        if (requirePasswordSetup && setupPasswordUrl != null)
            variables["SetupPasswordUrl"] = setupPasswordUrl;

        return new EmailData
        {
            Template = "AutomatonProjectInvitation",
            From = new EmailAccount
            {
                Email = string.IsNullOrWhiteSpace(configurationService.EmailConfig.FromEmail)
                    ? configurationService.EmailConfig.EmailAccount
                    : configurationService.EmailConfig.FromEmail
            },
            To = new[] { new EmailAccount { Email = request.Email, Name = user.Name } },
            Variables = variables
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

        await auditEventService.WriteAsync(
            action: "UpdatedRole",
            entityType: "ProjectMember",
            entityId: userId,
            message: $"Project member role was updated to {request.Role}",
            metadata: new
            {
                ProjectId = projectId,
                MemberUserId = userId,
                request.Role,
                UserId
            },
            cancellationToken: cancellationToken);


        return MapMemberToDto(membership, project.OwnerId);
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

        await auditEventService.WriteAsync(
            action: "RemovedMember",
            entityType: "ProjectMember",
            entityId: userId,
            message: "User was removed from project",
            metadata: new
            {
                ProjectId = projectId,
                MemberUserId = userId,
                UserId
            },
            cancellationToken: cancellationToken);

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

        return MapMemberToDto(membership, project?.OwnerId);
    }

    private static ProjectMemberDto MapMemberToDto(ProjectMember membership, Guid? projectOwnerId)
    {
        var user = membership.User
            ?? throw new InvalidOperationException($"Project member {membership.UserId} has no associated user.");
        return new ProjectMemberDto
        {
            ProjectId = membership.ProjectId,
            UserId = membership.UserId,
            UserName = user.UserName ?? string.Empty,
            Email = user.Email ?? string.Empty,
            Role = membership.Role,
            JoinedAt = membership.JoinedAt,
            InvitedBy = membership.InvitedBy,
            InvitedByName = membership.Inviter?.UserName,
            IsActive = membership.IsActive,
            IsOwner = projectOwnerId == membership.UserId
        };
    }
}

/// <summary>No account exists for the email; a server administrator has to invite the person first.</summary>
public sealed class ProjectMemberAccountMissingException(string email)
    : InvalidOperationException($"No account exists for {email}. Ask a server administrator to invite them first, then add them to the project.");
