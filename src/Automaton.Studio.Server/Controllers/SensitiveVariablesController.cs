using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SensitiveVariablesController : ControllerBase
{
    private readonly SensitiveVariableService sensitiveVariableService;
    private readonly ILogger<SensitiveVariablesController> logger;

    public SensitiveVariablesController(
        SensitiveVariableService sensitiveVariableService,
        ILogger<SensitiveVariablesController> logger)
    {
        this.sensitiveVariableService = sensitiveVariableService;
        this.logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<SensitiveVariableResponse>> CreateSensitiveVariable([FromBody] CreateSensitiveVariableRequest request)
    {
        var userId = GetUserId();

        try
        {
            var sensitiveVar = await sensitiveVariableService.CreateOrUpdateSensitiveVariableAsync(
                userId,
                request.Name,
                request.Value,
                request.ResourceType,
                request.ResourceId,
                request.VariableScope,
                request.Description,
                request.ExistingId);

            var response = new SensitiveVariableResponse
            {
                Id = sensitiveVar.Id,
                Name = sensitiveVar.Name,
                Description = sensitiveVar.Description,
                ResourceType = sensitiveVar.ResourceType,
                ResourceId = sensitiveVar.ResourceId,
                VariableScope = sensitiveVar.VariableScope,
                CreatedAt = sensitiveVar.CreatedAt,
                UpdatedAt = sensitiveVar.UpdatedAt,
                LastUsedAt = sensitiveVar.LastUsedAt
            };

            return CreatedAtAction(nameof(GetSensitiveVariable), new { id = sensitiveVar.Id }, response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating sensitive variable");
            return StatusCode(500, "An error occurred while creating the sensitive variable");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SensitiveVariableResponse>> GetSensitiveVariable(Guid id)
    {
        var userId = GetUserId();

        var sensitiveVar = await sensitiveVariableService.GetSensitiveVariablesForResourceAsync(
            string.Empty, Guid.Empty, userId); // This needs refactoring

        // For now, return NotFound
        // TODO: Implement GetSensitiveVariableByIdAsync method in service
        return NotFound();
    }

    [HttpPost("{id}/decrypt")]
    public async Task<ActionResult<DecryptedVariableResponse>> GetDecryptedValue(Guid id)
    {
        var userId = GetUserId();

        var decryptedValue = await sensitiveVariableService.GetDecryptedValueAsync(id, userId);

        if (decryptedValue == null)
        {
            return NotFound("Sensitive variable not found or access denied");
        }

        return Ok(new DecryptedVariableResponse { Value = decryptedValue });
    }

    [HttpGet("resource/{resourceType}/{resourceId}")]
    public async Task<ActionResult<List<SensitiveVariableResponse>>> GetSensitiveVariablesForResource(
        string resourceType,
        Guid resourceId)
    {
        var userId = GetUserId();

        var sensitiveVars = await sensitiveVariableService.GetSensitiveVariablesForResourceAsync(
            resourceType, resourceId, userId);

        var response = sensitiveVars.Select(sv => new SensitiveVariableResponse
        {
            Id = sv.Id,
            Name = sv.Name,
            Description = sv.Description,
            ResourceType = sv.ResourceType,
            ResourceId = sv.ResourceId,
            VariableScope = sv.VariableScope,
            CreatedAt = sv.CreatedAt,
            UpdatedAt = sv.UpdatedAt,
            LastUsedAt = sv.LastUsedAt
        }).ToList();

        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSensitiveVariable(Guid id)
    {
        var userId = GetUserId();

        var success = await sensitiveVariableService.DeleteSensitiveVariableAsync(id, userId);

        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid user ID");
        }
        return userId;
    }
}

// DTOs
public class CreateSensitiveVariableRequest
{
    public string Name { get; set; }
    public string Value { get; set; }
    public string? Description { get; set; }
    public string ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public string VariableScope { get; set; }
    public Guid? ExistingId { get; set; }
}

public class SensitiveVariableResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public string ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public string VariableScope { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class DecryptedVariableResponse
{
    public string Value { get; set; }
}
