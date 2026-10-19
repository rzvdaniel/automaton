using Automaton.Api.Models;
using Automaton.Api.Entities;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// API controller for managing organizations.
/// Provides endpoints for creating, listing, updating organizations,
/// managing members, and assigning apps to users.
/// </summary>
[Authorize]
public class OrganizationsController : BaseController
{
    private readonly IOrganizationService organizationService;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;
    private readonly ILogger<OrganizationsController> logger;

    public OrganizationsController(
        IOrganizationService organizationService,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        ILogger<OrganizationsController> logger)
    {
        this.organizationService  = organizationService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.logger               = logger;
    }

    // =====================================================
    // Organization CRUD
    // =====================================================

    /// <summary>
    /// List all organizations the current user is a member of.
    /// Returns basic organization information with member/project counts.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationListItem>>> ListOrganizations()
    {
        try
        {
            var organizations = await organizationService.ListUserOrganizationsAsync();
            return Ok(organizations);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing organizations for user");
            return StatusCode(500, new { message = "Error retrieving organizations" });
        }
    }

    /// <summary>
    /// Get detailed information about a specific organization.
    /// Includes member list, project counts, and organization settings.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<OrganizationDto>> GetOrganization(Guid id)
    {
        try
        {
            var organization = await organizationService.GetOrganizationAsync(id);
            
            if (organization == null)
            {
                return NotFound(new { message = "Organization not found" });
            }

            return Ok(organization);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error retrieving organization" });
        }
    }

    /// <summary>
    /// Create a new organization.
    /// The creator automatically becomes the owner with OrgAdmin role.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var organization = await organizationService.CreateOrganizationAsync(request);
            
            return CreatedAtAction(nameof(GetOrganization), new { id = organization.Id }, organization);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating organization");
            return StatusCode(500, new { message = "Error creating organization" });
        }
    }

    /// <summary>
    /// Update an existing organization.
    /// Only OrgAdmin can update organization settings.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<OrganizationDto>> UpdateOrganization(Guid id, [FromBody] UpdateOrganizationRequest request)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var organization = await organizationService.UpdateOrganizationAsync(id, request);
            
            if (organization == null)
            {
                return NotFound(new { message = "Organization not found or access denied" });
            }

            return Ok(organization);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error updating organization" });
        }
    }

    /// <summary>
    /// Delete an organization (soft delete).
    /// Only the organization owner can delete it.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteOrganization(Guid id)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var success = await organizationService.DeleteOrganizationAsync(id);
            
            if (!success)
            {
                return NotFound(new { message = "Organization not found or access denied" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error deleting organization" });
        }
    }

    // =====================================================
    // Membership Management
    // =====================================================

    /// <summary>
    /// Get all members of an organization.
    /// </summary>
    [HttpGet("{id}/members")]
    public async Task<ActionResult<IEnumerable<OrganizationMemberDto>>> GetMembers(Guid id)
    {
        try
        {
            var members = await organizationService.GetMembersAsync(id);
            return Ok(members);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving members for organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error retrieving members" });
        }
    }

    /// <summary>
    /// Invite a user to join the organization.
    /// Only OrgAdmin can invite members.
    /// </summary>
    [HttpPost("{id}/invitations")]
    public async Task<IActionResult> InviteMember(Guid id, [FromBody] InviteMemberRequest request)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var invitation = await organizationService.InviteMemberAsync(id, request);

            return Ok(new { 
                message = "Invitation sent successfully",
                expiresAt = invitation.ExpiresAt
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized invitation attempt");
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error inviting member to organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error sending invitation" });
        }
    }

    /// <summary>
    /// Accept an invitation to join an organization.
    /// Requires an authenticated user whose email matches the invitation.
    /// </summary>
    [Authorize]
    [HttpPost("invitations/accept")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var success = await organizationService.AcceptInvitationAsync(request.Token, GetUserId());
            
            if (!success)
            {
                return BadRequest(new { message = "Invalid or expired invitation token" });
            }

            return Ok(new { message = "Invitation accepted successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error accepting invitation");
            return StatusCode(500, new { message = "Error accepting invitation" });
        }
    }

    /// <summary>
    /// Get pending organization invitations for the current user.
    /// Returns all unexpired invitations that haven't been accepted yet.
    /// </summary>
    [HttpGet("invitations/pending")]
    public async Task<ActionResult<IEnumerable<PendingInvitationDto>>> GetPendingInvitations()
    {
        try
        {
            var invitations = await organizationService.GetPendingInvitationsAsync();
            return Ok(invitations);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving pending invitations for user {UserId}", GetUserId());
            return StatusCode(500, new { message = "Error retrieving pending invitations" });
        }
    }

    /// <summary>
    /// Decline an organization invitation.
    /// </summary>
    [HttpPost("invitations/{invitationId}/decline")]
    public async Task<IActionResult> DeclineInvitation(Guid invitationId)
    {
        try
        {
            var success = await organizationService.DeclineInvitationAsync(invitationId);
            
            if (!success)
            {
                return BadRequest(new { message = "Invalid or already consumed invitation" });
            }

            return Ok(new { message = "Invitation declined" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error declining invitation");
            return StatusCode(500, new { message = "Error declining invitation" });
        }
    }

    /// <summary>
    /// Remove a member from an organization.
    /// Only OrgAdmin can remove members.
    /// </summary>
    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var success = await organizationService.RemoveMemberAsync(id, userId);
            
            if (!success)
            {
                return NotFound(new { message = "Member not found or access denied" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing member from organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error removing member" });
        }
    }

    /// <summary>
    /// Leave an organization (self-removal).
    /// Any member can leave, except the owner.
    /// OrgAdmins must ensure another OrgAdmin exists before leaving.
    /// </summary>
    [HttpPost("{id}/leave")]
    public async Task<IActionResult> LeaveOrganization(Guid id)
    {
        try
        {
            var success = await organizationService.LeaveOrganizationAsync(id);
            
            if (!success)
            {
                return NotFound(new { message = "Organization not found or you are not a member" });
            }

            return Ok(new { message = "Successfully left the organization" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error leaving organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error leaving organization" });
        }
    }

    /// <summary>
    /// Update a member's role in an organization.
    /// Only OrgAdmin can update member roles.
    /// </summary>
    [HttpPut("{id}/members/{userId}")]
    public async Task<ActionResult<OrganizationMemberDto>> UpdateMemberRole(Guid id, Guid userId, [FromBody] UpdateOrganizationMemberRequest request)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var member = await organizationService.UpdateMemberRoleAsync(id, userId, request);
            
            if (member == null)
            {
                return NotFound(new { message = "Member not found" });
            }

            return Ok(member);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating member role in organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error updating member role" });
        }
    }

    // =====================================================
    // Projects
    // =====================================================

    /// <summary>
    /// Get all projects in the organization.
    /// Available to all organization members.
    /// </summary>
    [HttpGet("{id}/projects")]
    public async Task<ActionResult<IEnumerable<ProjectDto>>> GetOrganizationProjects(Guid id)
    {
        try
        {
            var projects = await organizationService.GetOrganizationProjectsAsync(id);
            return Ok(projects);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving projects for organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error retrieving organization projects" });
        }
    }

    // =====================================================
    // App Assignment
    // =====================================================

    /// <summary>
    /// Get all apps from projects in the organization.
    /// Available to all organization members.
    /// </summary>
    [HttpGet("{id}/apps")]
    public async Task<ActionResult> GetOrganizationApps(Guid id)
    {
        try
        {
            var apps = await organizationService.GetOrganizationAppsAsync(id);
            return Ok(apps.Select(ToAppSummary));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving apps for organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error retrieving organization apps" });
        }
    }

    /// <summary>Get safe metadata for apps assigned to the current user across active organizations.</summary>
    [HttpGet("apps/assigned")]
    public async Task<ActionResult<IReadOnlyList<OrganizationAppSummaryDto>>> GetCurrentUserAssignedApps(
        CancellationToken cancellationToken)
    {
        var apps = await organizationService.GetMyAssignedAppsAsync(GetUserId(), cancellationToken);
        return Ok(apps.Select(ToAppSummary));
    }

    /// <summary>
    /// Get all apps assigned to the current user in an organization.
    /// </summary>
    [HttpGet("{id}/apps/assigned")]
    public async Task<ActionResult> GetMyAssignedApps(Guid id)
    {
        try
        {
            var apps = await organizationService.GetAssignedAppsAsync(id, GetUserId());
            return Ok(apps.Select(ToAppSummary));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving assigned apps for organization {OrganizationId}", id);
            return StatusCode(500, new { message = "Error retrieving assigned apps" });
        }
    }

    private static OrganizationAppSummaryDto ToAppSummary(WebApp app)
        => new(
            app.Id,
            app.Name,
            app.Summary,
            app.Description,
            app.ThumbnailUrl,
            app.IsPublished);

    /// <summary>
    /// Assign an app to a user within the organization.
    /// Only OrgAdmin can assign apps.
    /// </summary>
    [HttpPost("{id}/apps/{appId}/assign")]
    public async Task<IActionResult> AssignApp(Guid id, Guid appId, [FromBody] AssignAppRequest request)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var assignment = await organizationService.AssignAppToUserAsync(id, appId, request.UserId);

            return Ok(new { message = "App assigned successfully", assignmentId = assignment.Id });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized app assignment attempt");
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error assigning app {AppId} in organization {OrganizationId}", appId, id);
            return StatusCode(500, new { message = "Error assigning app" });
        }
    }

    /// <summary>
    /// Unassign an app from a user.
    /// Only OrgAdmin can unassign apps.
    /// </summary>
    [HttpDelete("{id}/apps/{appId}/assign/{userId}")]
    public async Task<IActionResult> UnassignApp(Guid id, Guid appId, Guid userId)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var success = await organizationService.UnassignAppFromUserAsync(id, appId, userId);
            
            if (!success)
            {
                return NotFound(new { message = "Assignment not found or access denied" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unassigning app {AppId} in organization {OrganizationId}", appId, id);
            return StatusCode(500, new { message = "Error unassigning app" });
        }
    }

    /// <summary>
    /// Get all users assigned to a specific app in the organization.
    /// </summary>
    [HttpGet("{id}/apps/{appId}/users")]
    public async Task<ActionResult> GetAppAssignedUsers(Guid id, Guid appId)
    {
        try
        {
            var users = await organizationService.GetAppAssignedUsersAsync(id, appId);
            return Ok(users);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving assigned users for app {AppId} in organization {OrganizationId}", appId, id);
            return StatusCode(500, new { message = "Error retrieving assigned users" });
        }
    }
    
    /// <summary>
    /// Link an existing project to the organization.
    /// Only OrgAdmin can link projects, and user must be ProjectAdmin of the project.
    /// </summary>
    [HttpPut("{id}/projects/{projectId}")]
    public async Task<IActionResult> LinkProject(Guid id, Guid projectId)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var success = await organizationService.LinkProjectToOrganizationAsync(id, projectId);
            
            if (!success)
            {
                return NotFound(new { message = "Project not found" });
            }
            
            return Ok(new { message = "Project linked successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized project link attempt");
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking project {ProjectId} to organization {OrganizationId}", projectId, id);
            return StatusCode(500, new { message = "Error linking project" });
        }
    }
    
    /// <summary>
    /// Unlink a project from the organization (make it personal again).
    /// Only OrgAdmin can unlink projects.
    /// </summary>
    [HttpDelete("{id}/projects/{projectId}")]
    public async Task<IActionResult> UnlinkProject(Guid id, Guid projectId)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var success = await organizationService.UnlinkProjectFromOrganizationAsync(id, projectId);
            
            if (!success)
            {
                return NotFound(new { message = "Project not found in organization" });
            }
            
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error unlinking project {ProjectId} from organization {OrganizationId}", projectId, id);
            return StatusCode(500, new { message = "Error unlinking project" });
        }
    }
}
