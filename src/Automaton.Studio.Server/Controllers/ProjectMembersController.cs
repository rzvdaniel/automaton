using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// API controller for managing project members and their roles.
/// Provides endpoints for listing, adding, updating, and removing project members.
/// </summary>
[Authorize]
[Route("api/projects/{projectId}/members")]
public class ProjectMembersController : BaseController
{
    private readonly ProjectMemberService memberService;
    private readonly ILogger<ProjectMembersController> logger;

    public ProjectMembersController(
        ProjectMemberService memberService,
        ILogger<ProjectMembersController> logger)
    {
        this.memberService = memberService;
        this.logger = logger;
    }

    /// <summary>
    /// List all members of a project with their roles.
    /// Any project member can view the member list.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<IEnumerable<ProjectMemberDto>>> ListMembers(Guid projectId)
    {
        try
        {
            var members = await memberService.ListProjectMembersAsync(projectId);
            return Ok(members);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing members for project {ProjectId}", projectId);
            return StatusCode(500, new { message = "Error retrieving members" });
        }
    }

    /// <summary>
    /// Get details about a specific project member.
    /// Any project member can view member details.
    /// </summary>
    [HttpGet("{userId}")]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<ProjectMemberDto>> GetMember(Guid projectId, Guid userId)
    {
        try
        {
            var members = await memberService.ListProjectMembersAsync(projectId);
            var member = members.FirstOrDefault(m => m.UserId == userId);
            
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving member {UserId} from project {ProjectId}", 
                userId, projectId);
            return StatusCode(500, new { message = "Error retrieving member" });
        }
    }

    /// <summary>
    /// Add a new member to the project with specified role.
    /// Only ProjectAdmin can add members.
    /// If the email doesn't exist, a new account will be created automatically.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "ProjectAdmin")]
    public async Task<ActionResult<ProjectMemberDto>> AddMember(
        Guid projectId, 
        [FromBody] AddProjectMemberRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var member = await memberService.AddMemberAsync(projectId, request);
            
            logger.LogInformation(
                "Member added to project: ProjectId={ProjectId}, Email={Email}, Role={Role}, " +
                "CreatedAccount={Created}, AddedBy={AddedBy}",
                projectId, request.Email, request.Role, 
                request.CreateAccountIfNotExists, GetUserId());

            return CreatedAtAction(
                nameof(GetMember), 
                new { projectId, userId = member.UserId }, 
                member);
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
            logger.LogError(ex, "Error adding member to project {ProjectId}", projectId);
            return StatusCode(500, new { message = "Error adding member" });
        }
    }

    /// <summary>
    /// Update a member's role in the project.
    /// Only ProjectAdmin can update member roles.
    /// Cannot change the owner's role or remove the owner.
    /// </summary>
    [HttpPut("{userId}")]
    [Authorize(Policy = "ProjectAdmin")]
    public async Task<ActionResult<ProjectMemberDto>> UpdateMemberRole(
        Guid projectId,
        Guid userId,
        [FromBody] UpdateProjectMemberRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var member = await memberService.UpdateMemberRoleAsync(projectId, userId, request);
            
            if (member == null)
            {
                return NotFound(new { message = "Member not found" });
            }

            logger.LogInformation(
                "Member role updated: ProjectId={ProjectId}, UserId={UserId}, NewRole={Role}, UpdatedBy={UpdatedBy}",
                projectId, userId, request.Role, GetUserId());

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
            logger.LogError(ex, "Error updating member {UserId} in project {ProjectId}", 
                userId, projectId);
            return StatusCode(500, new { message = "Error updating member" });
        }
    }

    /// <summary>
    /// Remove a member from the project.
    /// Only ProjectAdmin can remove members.
    /// Cannot remove the project owner.
    /// This is a soft delete - the membership is marked as inactive.
    /// </summary>
    [HttpDelete("{userId}")]
    [Authorize(Policy = "ProjectAdmin")]
    public async Task<ActionResult> RemoveMember(Guid projectId, Guid userId)
    {
        try
        {
            var success = await memberService.RemoveMemberAsync(projectId, userId);
            
            if (!success)
            {
                return NotFound(new { message = "Member not found" });
            }

            logger.LogInformation(
                "Member removed from project: ProjectId={ProjectId}, UserId={UserId}, RemovedBy={RemovedBy}",
                projectId, userId, GetUserId());

            return NoContent();
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
            logger.LogError(ex, "Error removing member {UserId} from project {ProjectId}", 
                userId, projectId);
            return StatusCode(500, new { message = "Error removing member" });
        }
    }
}
