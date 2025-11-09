using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly ProjectService projectService;
    private readonly ProjectContextService projectContextService;
    private readonly ILogger<ProjectsController> logger;

    public ProjectsController(
        ProjectService projectService,
        ProjectContextService projectContextService,
        ILogger<ProjectsController> logger)
    {
        this.projectService = projectService;
        this.projectContextService = projectContextService;
        this.logger = logger;
    }

    /// <summary>
    /// Get all projects the current user belongs to
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProjectModel>>> GetAll()
    {
        var projects = await projectService.ListAsync();
        return Ok(projects);
    }

    /// <summary>
    /// Get a specific project by ID
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectModel>> Get(Guid id)
    {
        var project = await projectService.GetAsync(id);
        if (project == null)
        {
            return NotFound(new { message = "Project not found or you don't have access" });
        }

        return Ok(project);
    }

    /// <summary>
    /// Create a new project
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Guid>> Create([FromBody] ProjectCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Project name is required" });
        }

        var projectId = await projectService.CreateAsync(request);
        
        // Set as current project for the user
        await projectContextService.SetCurrentProjectIdAsync(projectId);

        return CreatedAtAction(nameof(Get), new { id = projectId }, projectId);
    }

    /// <summary>
    /// Update a project (requires Owner role)
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ProjectUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Project name is required" });
        }

        var success = await projectService.UpdateAsync(id, request);
        if (!success)
        {
            return NotFound(new { message = "Project not found or you don't have permission to update it" });
        }

        return NoContent();
    }

    /// <summary>
    /// Delete a project (requires admin/owner)
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var success = await projectService.DeleteAsync(id);
        if (!success)
        {
            return NotFound(new { message = "Project not found or you don't have permission to delete it" });
        }

        return NoContent();
    }

    /// <summary>
    /// Get the current active project for the user
    /// </summary>
    [HttpGet("current")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Guid?>> GetCurrentProject()
    {
        var currentProjectId = await projectContextService.GetCurrentProjectIdAsync();
        if (currentProjectId == null)
        {
            return NotFound(new { message = "No current project set. User may not belong to any projects." });
        }

        return Ok(new { projectId = currentProjectId });
    }

    /// <summary>
    /// Set the current active project for the user
    /// </summary>
    [HttpPost("current")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetCurrentProject([FromBody] SetCurrentProjectRequest request)
    {
        var success = await projectContextService.SetCurrentProjectIdAsync(request.ProjectId);
        if (!success)
        {
            return BadRequest(new { message = "Cannot set current project. You may not have access to this project." });
        }

        return NoContent();
    }

    /// <summary>
    /// Get all members of a project
    /// </summary>
    [HttpGet("{id}/members")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<ProjectMemberModel>>> GetMembers(Guid id)
    {
        var members = await projectService.ListMembersAsync(id);
        return Ok(members);
    }

    /// <summary>
    /// Add a member to a project (requires Owner role)
    /// </summary>
    [HttpPost("{id}/members")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddProjectMemberRequest request)
    {
        var success = await projectService.AddMemberAsync(id, request);
        if (!success)
        {
            return BadRequest(new { message = "Cannot add member. You may not have Owner role or the user is already a member." });
        }

        return NoContent();
    }

    /// <summary>
    /// Update a member's role in a project (requires Owner role)
    /// </summary>
    [HttpPut("{id}/members/{memberId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMemberRole(Guid id, Guid memberId, [FromBody] UpdateProjectMemberRequest request)
    {
        var success = await projectService.UpdateMemberRoleAsync(id, memberId, request);
        if (!success)
        {
            return BadRequest(new { message = "Cannot update member role. You may not have Owner role or the member doesn't exist." });
        }

        return NoContent();
    }

    /// <summary>
    /// Remove a member from a project (requires Owner role)
    /// </summary>
    [HttpDelete("{id}/members/{memberId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId)
    {
        var success = await projectService.RemoveMemberAsync(id, memberId);
        if (!success)
        {
            return BadRequest(new { message = "Cannot remove member. You may not have Owner role or the member doesn't exist." });
        }

        return NoContent();
    }
}

public class SetCurrentProjectRequest
{
    public Guid ProjectId { get; set; }
}
