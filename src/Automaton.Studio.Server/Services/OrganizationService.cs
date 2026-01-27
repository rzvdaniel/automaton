using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Service for managing organizations, members, and app assignments
/// </summary>
public class OrganizationService : IOrganizationService
{
    private readonly ApplicationDbContext dbContext;
    private readonly UserContextService userContextService;
    private readonly Serilog.ILogger logger;

    private Guid UserId => userContextService.GetUserId();

    public OrganizationService(
        ApplicationDbContext dbContext,
        UserContextService userContextService)
    {
        this.dbContext = dbContext;
        this.userContextService = userContextService;
        this.logger = Serilog.Log.ForContext<OrganizationService>();
    }

    // =====================================================
    // Organization CRUD
    // =====================================================

    public async Task<IEnumerable<OrganizationListItem>> ListUserOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        var organizations = await dbContext.OrganizationMembers
            .Where(om => om.UserId == UserId && om.IsActive)
            .Include(om => om.Organization)
            .Select(om => new OrganizationListItem
            {
                Id = om.Organization.Id,
                Name = om.Organization.Name,
                Description = om.Organization.Description,
                Slug = om.Organization.Slug,
                LogoUrl = om.Organization.LogoUrl,
                Role = om.Role,
                MemberCount = om.Organization.Members.Count(m => m.IsActive),
                ProjectCount = om.Organization.Projects.Count(p => p.IsActive),
                UpdatedAt = om.Organization.UpdatedAt ?? om.Organization.CreatedAt
            })
            .OrderByDescending(o => o.UpdatedAt)
            .ToListAsync(cancellationToken);

        logger.Information("Listed {Count} organizations for user {UserId}", organizations.Count, UserId);
        return organizations;
    }

    public async Task<OrganizationDto?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Check if user can access this organization
        if (!await CanAccessOrganizationAsync(organizationId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to access organization {OrganizationId} without membership", UserId, organizationId);
            return null;
        }

        var organization = await dbContext.Organizations
            .Include(o => o.Owner)
            .Include(o => o.Members)
            .Include(o => o.Projects)
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.IsActive, cancellationToken);

        if (organization == null)
        {
            return null;
        }

        // Get user's role in the organization
        var userRole = await GetUserRoleAsync(organizationId, UserId, cancellationToken);

        return new OrganizationDto
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            OwnerId = organization.OwnerId,
            OwnerName = organization.Owner?.UserName ?? "Unknown",
            Slug = organization.Slug,
            AllowPublicSignup = organization.AllowPublicSignup,
            RequireEmailDomain = organization.RequireEmailDomain,
            LogoUrl = organization.LogoUrl,
            PrimaryColor = organization.PrimaryColor,
            CreatedAt = organization.CreatedAt,
            UpdatedAt = organization.UpdatedAt,
            IsActive = organization.IsActive,
            MemberCount = organization.Members.Count(m => m.IsActive),
            ProjectCount = organization.Projects.Count(p => p.IsActive),
            CurrentUserRole = userRole
        };
    }

    public async Task<OrganizationDto> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        // Generate slug if not provided
        var slug = request.Slug ?? GenerateSlug(request.Name);

        // Ensure slug is unique
        var existingOrg = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Slug == slug, cancellationToken);

        if (existingOrg != null)
        {
            // Append a number to make it unique
            var counter = 1;
            while (await dbContext.Organizations.AnyAsync(o => o.Slug == $"{slug}-{counter}", cancellationToken))
            {
                counter++;
            }
            slug = $"{slug}-{counter}";
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            OwnerId = UserId,
            Slug = slug,
            AllowPublicSignup = request.AllowPublicSignup,
            RequireEmailDomain = request.RequireEmailDomain,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        dbContext.Organizations.Add(organization);

        // Add the creator as OrgAdmin
        var ownerMembership = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            UserId = UserId,
            Role = OrganizationRole.OrgAdmin,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };

        dbContext.OrganizationMembers.Add(ownerMembership);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("User {UserId} created organization {OrganizationId} ({Name})", UserId, organization.Id, organization.Name);

        return new OrganizationDto
        {
            Id = organization.Id,
            Name = organization.Name,
            Description = organization.Description,
            OwnerId = organization.OwnerId,
            OwnerName = "You",
            Slug = organization.Slug,
            AllowPublicSignup = organization.AllowPublicSignup,
            RequireEmailDomain = organization.RequireEmailDomain,
            CreatedAt = organization.CreatedAt,
            UpdatedAt = organization.UpdatedAt,
            IsActive = organization.IsActive,
            MemberCount = 1,
            ProjectCount = 0,
            CurrentUserRole = OrganizationRole.OrgAdmin
        };
    }

    public async Task<OrganizationDto?> UpdateOrganizationAsync(Guid organizationId, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        // Only OrgAdmin can update organization
        if (!await IsOrgAdminAsync(organizationId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to update organization {OrganizationId} without admin rights", UserId, organizationId);
            return null;
        }

        var organization = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.IsActive, cancellationToken);

        if (organization == null)
        {
            return null;
        }

        organization.Name = request.Name;
        organization.Description = request.Description;
        organization.AllowPublicSignup = request.AllowPublicSignup;
        organization.RequireEmailDomain = request.RequireEmailDomain;
        organization.LogoUrl = request.LogoUrl;
        organization.PrimaryColor = request.PrimaryColor;
        organization.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("User {UserId} updated organization {OrganizationId}", UserId, organizationId);

        return await GetOrganizationAsync(organizationId, cancellationToken);
    }

    public async Task<bool> DeleteOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Only the owner can delete an organization
        var organization = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.IsActive, cancellationToken);

        if (organization == null || organization.OwnerId != UserId)
        {
            logger.Warning("User {UserId} attempted to delete organization {OrganizationId} without ownership", UserId, organizationId);
            return false;
        }

        // Soft delete
        organization.IsActive = false;
        organization.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("User {UserId} deleted organization {OrganizationId}", UserId, organizationId);
        return true;
    }

    // =====================================================
    // Membership Management
    // =====================================================

    public async Task<IEnumerable<OrganizationMemberDto>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Check if user can access this organization
        if (!await CanAccessOrganizationAsync(organizationId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to list members of organization {OrganizationId} without access", UserId, organizationId);
            return Enumerable.Empty<OrganizationMemberDto>();
        }

        var organization = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.IsActive, cancellationToken);

        if (organization == null)
        {
            return Enumerable.Empty<OrganizationMemberDto>();
        }

        var members = await dbContext.OrganizationMembers
            .Where(om => om.OrganizationId == organizationId && om.IsActive)
            .Include(om => om.User)
            .Include(om => om.Inviter)
            .Select(om => new OrganizationMemberDto
            {
                Id = om.Id,
                OrganizationId = om.OrganizationId,
                UserId = om.UserId,
                UserName = om.User != null ? om.User.UserName ?? "" : "",
                Email = om.User != null ? om.User.Email ?? "" : "",
                Role = om.Role,
                JoinedAt = om.JoinedAt,
                InvitedBy = om.InvitedBy,
                InvitedByName = om.Inviter != null ? om.Inviter.UserName : null,
                IsActive = om.IsActive,
                IsOwner = om.UserId == organization.OwnerId
            })
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);

        return members;
    }

    public async Task<OrganizationRole?> GetUserRoleAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await dbContext.OrganizationMembers
            .FirstOrDefaultAsync(om => om.OrganizationId == organizationId && om.UserId == userId && om.IsActive, cancellationToken);

        return member?.Role;
    }

    public async Task<OrganizationInvitation> InviteMemberAsync(Guid organizationId, InviteMemberRequest request, CancellationToken cancellationToken = default)
    {
        // Only OrgAdmin or Developer can invite members
        var userRole = await GetUserRoleAsync(organizationId, UserId, cancellationToken);
        if (userRole != OrganizationRole.OrgAdmin && userRole != OrganizationRole.Developer)
        {
            throw new UnauthorizedAccessException("Only OrgAdmin or Developer can invite members");
        }

        // Check if email domain is allowed
        var organization = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.IsActive, cancellationToken);

        if (organization == null)
        {
            throw new InvalidOperationException("Organization not found");
        }

        if (!string.IsNullOrEmpty(organization.RequireEmailDomain))
        {
            var emailDomain = request.Email.Split('@').LastOrDefault();
            if (!organization.RequireEmailDomain.Equals(emailDomain, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Email must be from domain {organization.RequireEmailDomain}");
            }
        }

        // Create invitation token
        var token = GenerateSecureToken();
        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = request.Email,
            Role = request.Role,
            Token = token,
            InvitedBy = UserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7), // Expires in 7 days
            IsConsumed = false
        };

        dbContext.OrganizationInvitations.Add(invitation);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("User {UserId} invited {Email} to organization {OrganizationId} as {Role}", 
            UserId, request.Email, organizationId, request.Role);

        return invitation;
    }

    public async Task<bool> AcceptInvitationAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await dbContext.OrganizationInvitations
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.Token == token && !i.IsConsumed, cancellationToken);

        if (invitation == null)
        {
            logger.Warning("Invalid or already consumed invitation token");
            return false;
        }

        if (invitation.ExpiresAt < DateTime.UtcNow)
        {
            logger.Warning("Invitation token has expired");
            return false;
        }

        // Get user by email
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == invitation.Email, cancellationToken);
        if (user == null)
        {
            logger.Warning("User with email {Email} not found", invitation.Email);
            return false;
        }

        // Check if already a member
        var existingMember = await dbContext.OrganizationMembers
            .FirstOrDefaultAsync(om => om.OrganizationId == invitation.OrganizationId && om.UserId == user.Id, cancellationToken);

        if (existingMember != null)
        {
            if (!existingMember.IsActive)
            {
                // Reactivate membership
                existingMember.IsActive = true;
                existingMember.Role = invitation.Role;
                existingMember.JoinedAt = DateTime.UtcNow;
            }
            else
            {
                logger.Warning("User {UserId} is already a member of organization {OrganizationId}", user.Id, invitation.OrganizationId);
                return false;
            }
        }
        else
        {
            // Add new member
            var member = new OrganizationMember
            {
                Id = Guid.NewGuid(),
                OrganizationId = invitation.OrganizationId,
                UserId = user.Id,
                Role = invitation.Role,
                InvitedBy = invitation.InvitedBy,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };

            dbContext.OrganizationMembers.Add(member);
        }

        // Mark invitation as consumed
        invitation.IsConsumed = true;
        invitation.AcceptedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("User {UserId} accepted invitation to organization {OrganizationId}", user.Id, invitation.OrganizationId);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Only OrgAdmin can remove members
        if (!await IsOrgAdminAsync(organizationId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to remove member from organization {OrganizationId} without admin rights", UserId, organizationId);
            return false;
        }

        // Cannot remove the owner
        var organization = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId && o.IsActive, cancellationToken);

        if (organization == null || organization.OwnerId == userId)
        {
            logger.Warning("Cannot remove organization owner");
            return false;
        }

        var member = await dbContext.OrganizationMembers
            .FirstOrDefaultAsync(om => om.OrganizationId == organizationId && om.UserId == userId && om.IsActive, cancellationToken);

        if (member == null)
        {
            return false;
        }

        // Soft delete
        member.IsActive = false;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("User {UserId} removed user {RemovedUserId} from organization {OrganizationId}", UserId, userId, organizationId);
        return true;
    }

    // =====================================================
    // App Assignment
    // =====================================================

    public async Task<OrganizationAppAssignment> AssignAppToUserAsync(Guid organizationId, Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Only OrgAdmin can assign apps
        if (!await IsOrgAdminAsync(organizationId, UserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("Only OrgAdmin can assign apps");
        }

        // Verify app belongs to organization (via project)
        var appProject = await dbContext.ProjectApps
            .Include(pa => pa.Project)
            .FirstOrDefaultAsync(pa => pa.WebAppId == appId && pa.Project.OrganizationId == organizationId, cancellationToken);

        if (appProject == null)
        {
            throw new InvalidOperationException("App does not belong to this organization");
        }

        // Verify user is a member
        var isMember = await dbContext.OrganizationMembers
            .AnyAsync(om => om.OrganizationId == organizationId && om.UserId == userId && om.IsActive, cancellationToken);

        if (!isMember)
        {
            throw new InvalidOperationException("User is not a member of this organization");
        }

        // Check if already assigned
        var existing = await dbContext.OrganizationAppAssignments
            .FirstOrDefaultAsync(oa => oa.OrganizationId == organizationId && oa.AppId == appId && oa.UserId == userId, cancellationToken);

        if (existing != null)
        {
            if (!existing.IsActive)
            {
                // Reactivate
                existing.IsActive = true;
                existing.AssignedAt = DateTime.UtcNow;
                existing.AssignedBy = UserId;
                await dbContext.SaveChangesAsync(cancellationToken);
                return existing;
            }
            else
            {
                throw new InvalidOperationException("App already assigned to this user");
            }
        }

        // Create assignment
        var assignment = new OrganizationAppAssignment
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AppId = appId,
            UserId = userId,
            AssignedBy = UserId,
            AssignedAt = DateTime.UtcNow,
            IsActive = true
        };

        dbContext.OrganizationAppAssignments.Add(assignment);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("User {UserId} assigned app {AppId} to user {TargetUserId} in organization {OrganizationId}", 
            UserId, appId, userId, organizationId);

        return assignment;
    }

    public async Task<bool> UnassignAppFromUserAsync(Guid organizationId, Guid appId, Guid userId, CancellationToken cancellationToken = default)
    {
        // Only OrgAdmin can unassign apps
        if (!await IsOrgAdminAsync(organizationId, UserId, cancellationToken))
        {
            logger.Warning("User {UserId} attempted to unassign app without admin rights", UserId);
            return false;
        }

        var assignment = await dbContext.OrganizationAppAssignments
            .FirstOrDefaultAsync(oa => oa.OrganizationId == organizationId && oa.AppId == appId && oa.UserId == userId && oa.IsActive, cancellationToken);

        if (assignment == null)
        {
            return false;
        }

        // Soft delete
        assignment.IsActive = false;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.Information("User {UserId} unassigned app {AppId} from user {TargetUserId} in organization {OrganizationId}", 
            UserId, appId, userId, organizationId);

        return true;
    }

    public async Task<IEnumerable<Project>> GetOrganizationProjectsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Verify user has access to organization
        if (!await CanAccessOrganizationAsync(organizationId, UserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("You do not have access to this organization");
        }

        // Get all projects in this organization
        var projects = await dbContext.Projects
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .Include(p => p.Owner)
            .Include(p => p.Members)
            .OrderByDescending(p => p.Updated)
            .ToListAsync(cancellationToken);

        return projects;
    }

    public async Task<IEnumerable<WebApp>> GetOrganizationAppsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        // Verify user has access to organization
        if (!await CanAccessOrganizationAsync(organizationId, UserId, cancellationToken))
        {
            throw new UnauthorizedAccessException("You do not have access to this organization");
        }

        // Get all apps from projects in this organization
        var apps = await dbContext.ProjectApps
            .Where(pa => pa.Project.OrganizationId == organizationId && pa.Project.IsActive)
            .Include(pa => pa.WebApp)
            .Select(pa => pa.WebApp!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return apps;
    }

    public async Task<IEnumerable<WebApp>> GetAssignedAppsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var apps = await dbContext.OrganizationAppAssignments
            .Where(oa => oa.OrganizationId == organizationId && oa.UserId == userId && oa.IsActive)
            .Include(oa => oa.App)
            .Select(oa => oa.App!)
            .ToListAsync(cancellationToken);

        return apps;
    }

    public async Task<IEnumerable<ApplicationUser>> GetAppAssignedUsersAsync(Guid organizationId, Guid appId, CancellationToken cancellationToken = default)
    {
        var users = await dbContext.OrganizationAppAssignments
            .Where(oa => oa.OrganizationId == organizationId && oa.AppId == appId && oa.IsActive)
            .Include(oa => oa.User)
            .Select(oa => oa.User!)
            .ToListAsync(cancellationToken);

        return users;
    }

    // =====================================================
    // Helper Methods
    // =====================================================

    private async Task<bool> CanAccessOrganizationAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.OrganizationMembers
            .AnyAsync(om => om.OrganizationId == organizationId && om.UserId == userId && om.IsActive, cancellationToken);
    }

    private async Task<bool> IsOrgAdminAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        var member = await dbContext.OrganizationMembers
            .FirstOrDefaultAsync(om => om.OrganizationId == organizationId && om.UserId == userId && om.IsActive, cancellationToken);

        return member?.Role == OrganizationRole.OrgAdmin;
    }

    private static string GenerateSlug(string name)
    {
        // Convert to lowercase, replace spaces with hyphens, remove special characters
        var slug = name.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');

        // Remove non-alphanumeric characters except hyphens
        slug = new string(slug.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

        // Remove consecutive hyphens
        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Trim('-');
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, 64);
    }
}
