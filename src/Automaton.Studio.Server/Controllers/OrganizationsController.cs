using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// API controller for managing organizations.
/// Provides endpoints for creating, listing, updating organizations,
/// managing members, and assigning apps to users.
/// </summary>
[Authorize]
public class OrganizationsController : BaseController
{
    private readonly IOrganizationService organizationService;
    private readonly ILogger<OrganizationsController> logger;

    public OrganizationsController(
        IOrganizationService organizationService,
        ILogger<OrganizationsController> logger)
    {
        this.organizationService = organizationService;
        this.logger = logger;
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
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var organization = await organizationService.CreateOrganizationAsync(request);
            
            logger.LogInformation("Organization created: {OrganizationId} by user {UserId}", 
                organization.Id, GetUserId());

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

            logger.LogInformation("Organization updated: {OrganizationId} by user {UserId}", 
                id, GetUserId());

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
        try
        {
            var success = await organizationService.DeleteOrganizationAsync(id);
            
            if (!success)
            {
                return NotFound(new { message = "Organization not found or access denied" });
            }

            logger.LogInformation("Organization deleted: {OrganizationId} by user {UserId}", 
                id, GetUserId());

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
    /// Only OrgAdmin and Developer can invite members.
    /// </summary>
    [HttpPost("{id}/invitations")]
    public async Task<IActionResult> InviteMember(Guid id, [FromBody] InviteMemberRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var invitation = await organizationService.InviteMemberAsync(id, request);

            logger.LogInformation("User invited to organization {OrganizationId}: {Email} as {Role}", 
                id, request.Email, request.Role);

            return Ok(new { 
                message = "Invitation sent successfully",
                token = invitation.Token,
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
    /// </summary>
    [HttpPost("invitations/accept")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var success = await organizationService.AcceptInvitationAsync(request.Token);
            
            if (!success)
            {
                return BadRequest(new { message = "Invalid or expired invitation token" });
            }

            logger.LogInformation("User {UserId} accepted organization invitation", GetUserId());

            return Ok(new { message = "Invitation accepted successfully" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error accepting invitation");
            return StatusCode(500, new { message = "Error accepting invitation" });
        }
    }

    /// <summary>
    /// Remove a member from an organization.
    /// Only OrgAdmin can remove members.
    /// </summary>
    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId)
    {
        try
        {
            var success = await organizationService.RemoveMemberAsync(id, userId);
            
            if (!success)
            {
                return NotFound(new { message = "Member not found or access denied" });
            }

            logger.LogInformation("User {RemovedUserId} removed from organization {OrganizationId} by {UserId}", 
                userId, id, GetUserId());

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

    // =====================================================
    // App Assignment
    // =====================================================

    /// <summary>
    /// Get all apps assigned to the current user in an organization.
    /// </summary>
    [HttpGet("{id}/apps/assigned")]
    public async Task<ActionResult> GetMyAssignedApps(Guid id)
    {
        try
        {
            var apps = await organizationService.GetAssignedAppsAsync(id, GetUserId());
            return Ok(apps);
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

    /// <summary>
    /// Assign an app to a user within the organization.
    /// Only OrgAdmin can assign apps.
    /// </summary>
    [HttpPost("{id}/apps/{appId}/assign")]
    public async Task<IActionResult> AssignApp(Guid id, Guid appId, [FromBody] AssignAppRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var assignment = await organizationService.AssignAppToUserAsync(id, appId, request.UserId);

            logger.LogInformation("App {AppId} assigned to user {UserId} in organization {OrganizationId}", 
                appId, request.UserId, id);

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
        try
        {
            var success = await organizationService.UnassignAppFromUserAsync(id, appId, userId);
            
            if (!success)
            {
                return NotFound(new { message = "Assignment not found or access denied" });
            }

            logger.LogInformation("App {AppId} unassigned from user {UserId} in organization {OrganizationId}", 
                appId, userId, id);

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
}
