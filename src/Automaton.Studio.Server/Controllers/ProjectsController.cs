using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// API controller for managing projects/organizations.
/// Provides endpoints for creating, listing, updating, and deleting projects.
/// </summary>
[Authorize]
public class ProjectsController : BaseController
{
    private readonly ProjectService projectService;
    private readonly ILogger<ProjectsController> logger;

    public ProjectsController(
        ProjectService projectService,
        ILogger<ProjectsController> logger)
    {
        this.projectService = projectService;
        this.logger = logger;
    }

    /// <summary>
    /// List all projects the current user is a member of.
    /// Returns basic project information with member/resource counts.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectListItem>>> ListProjects()
    {
        try
        {
            var projects = await projectService.ListUserProjectsAsync();
            return Ok(projects);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing projects for user");
            return StatusCode(500, new { message = "Error retrieving projects" });
        }
    }

    /// <summary>
    /// Get detailed information about a specific project.
    /// Includes member list, resource counts, and project settings.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "ProjectMember")]
    public async Task<ActionResult<ProjectDto>> GetProject(Guid id)
    {
        try
        {
            var project = await projectService.GetProjectAsync(id);
            
            if (project == null)
            {
                return NotFound(new { message = "Project not found" });
            }

            return Ok(project);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving project {ProjectId}", id);
            return StatusCode(500, new { message = "Error retrieving project" });
        }
    }

    /// <summary>
    /// Create a new project.
    /// The creator automatically becomes the owner with ProjectAdmin role.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ProjectDto>> CreateProject([FromBody] CreateProjectRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var project = await projectService.CreateProjectAsync(request);
            
            logger.LogInformation("Project created: {ProjectId} by user {UserId}", 
                project.Id, GetUserId());

            return CreatedAtAction(nameof(GetProject), new { id = project.Id }, project);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating project");
            return StatusCode(500, new { message = "Error creating project" });
        }
    }

    /// <summary>
    /// Update project details (display name, description, active status).
    /// Only ProjectAdmin can update project settings.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "ProjectAdmin")]
    public async Task<ActionResult<ProjectDto>> UpdateProject(Guid id, [FromBody] UpdateProjectRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var project = await projectService.UpdateProjectAsync(id, request);
            
            if (project == null)
            {
                return NotFound(new { message = "Project not found" });
            }

            logger.LogInformation("Project updated: {ProjectId} by user {UserId}", 
                id, GetUserId());

            return Ok(project);
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
            logger.LogError(ex, "Error updating project {ProjectId}", id);
            return StatusCode(500, new { message = "Error updating project" });
        }
    }

    /// <summary>
    /// Delete (deactivate) a project.
    /// Only the project owner can delete the project.
    /// This is a soft delete - the project is marked as inactive.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "ProjectAdmin")]
    public async Task<ActionResult> DeleteProject(Guid id)
    {
        try
        {
            var success = await projectService.DeleteProjectAsync(id);
            
            if (!success)
            {
                return NotFound(new { message = "Project not found" });
            }

            logger.LogInformation("Project deleted: {ProjectId} by user {UserId}", 
                id, GetUserId());

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting project {ProjectId}", id);
            return StatusCode(500, new { message = "Error deleting project" });
        }
    }

    /// <summary>
    /// Reactivate a previously deleted (deactivated) project.
    /// Only the project owner can reactivate the project.
    /// </summary>
    [HttpPost("{id}/reactivate")]
    [Authorize(Policy = "ProjectAdmin")]
    public async Task<ActionResult> ReactivateProject(Guid id)
    {
        try
        {
            var success = await projectService.ReactivateProjectAsync(id);
            
            if (!success)
            {
                return NotFound(new { message = "Project not found" });
            }

            logger.LogInformation("Project reactivated: {ProjectId} by user {UserId}", 
                id, GetUserId());

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reactivating project {ProjectId}", id);
            return StatusCode(500, new { message = "Error reactivating project" });
        }
    }

    /// <summary>
    /// Check if current user is a member of the project and get their role.
    /// </summary>
    [HttpGet("{id}/membership")]
    public async Task<ActionResult<object>> GetMembership(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var role = await projectService.GetUserRoleAsync(id, userId);
            
            if (role == null)
            {
                return Ok(new { isMember = false, role = (string)null });
            }

            return Ok(new { isMember = true, role = role.ToString() });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking membership for project {ProjectId}", id);
            return StatusCode(500, new { message = "Error checking membership" });
        }
    }
}
