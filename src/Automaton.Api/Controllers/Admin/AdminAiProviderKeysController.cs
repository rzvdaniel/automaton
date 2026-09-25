using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/ai-providers")]
[Authorize(Policy = "Administrators")]
public class AdminAiProviderKeysController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly AiApiKeyService aiApiKeyService;
    private readonly IAuditEventService auditEventService;
    private readonly AdminAiProviderCreationService creationService;

    public AdminAiProviderKeysController(
        ApplicationDbContext dbContext,
        AiApiKeyService aiApiKeyService,
        IAuditEventService auditEventService,
        AdminAiProviderCreationService creationService)
    {
        this.dbContext = dbContext;
        this.aiApiKeyService = aiApiKeyService;
        this.auditEventService = auditEventService;
        this.creationService = creationService;
    }
    #region API Keys

    /// <summary>
    /// Get all API keys for a provider
    /// </summary>
    [HttpGet("{providerId}/keys")]
    public async Task<ActionResult<IEnumerable<AiApiKeyAdminResponse>>> GetProviderKeys(Guid providerId)
    {
        var provider = await dbContext.AiProviders.FindAsync(providerId);
        if (provider == null)
            return NotFound("Provider not found");

        var keys = await dbContext.AiApiKeys
            .Where(k => k.AiProviderId == providerId && k.CreatedByUserId == null)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new AiApiKeyAdminResponse
            {
                Id = k.Id,
                Name = k.Name,
                ProviderName = k.ProviderName,
                Description = k.Description,
                CreatedAt = k.CreatedAt,
                LastUsedAt = k.LastUsedAt,
                IsActive = k.IsActive,
                IsSystemKey = k.CreatedByUserId == null
            })
            .ToListAsync();

        return Ok(keys);
    }

    /// <summary>
    /// Create a new API key for a provider
    /// </summary>
    [HttpPost("{providerId}/keys")]
    public async Task<ActionResult<AiApiKeyAdminResponse>> CreateProviderKey(Guid providerId, [FromBody] AdminCreateAiApiKeyRequest request)
    {
        var result = await creationService.CreateProviderKeyAsync(providerId, request);
        if (result.ResourceNotFound)
            return NotFound("Provider not found");
        if (result.Error is not null)
            return BadRequest(result.Error);

        return CreatedAtAction(nameof(GetProviderKeys), new { providerId }, result.Value);
    }

    /// <summary>
    /// Update an existing API key
    /// </summary>
    [HttpPut("~/api/admin/ai-keys/{id}")]
    public async Task<IActionResult> UpdateApiKey(Guid id, [FromBody] AdminUpdateAiApiKeyRequest request)
    {
        var key = await dbContext.AiApiKeys.FindAsync(id);

        if (key == null)
            return NotFound();

        if (request.Name != null) key.Name = request.Name;
        if (request.Description != null) key.Description = request.Description;
        if (request.IsActive.HasValue) key.IsActive = request.IsActive.Value;

        // If new API key is provided, update it
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            key.EncryptedApiKey = aiApiKeyService.EncryptApiKey(request.ApiKey);
        }

        await dbContext.SaveChangesAsync();

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiApiKeyUpdated,
            Action = AuditActions.Updated,
            EntityType = AuditEntityTypes.AiApiKey,
            EntityId = key.Id,
            Message = $"AI API key '{key.Name}' was updated",
            Metadata = new
            {
                key.Name,
                key.ProviderName,
                key.Description,
                key.IsActive,
                RotatedSecret = !string.IsNullOrWhiteSpace(request.ApiKey)
            }
        });

        return NoContent();
    }

    /// <summary>
    /// Delete an API key
    /// </summary>
    [HttpDelete("~/api/admin/ai-keys/{id}")]
    public async Task<IActionResult> DeleteApiKey(Guid id)
    {
        var key = await dbContext.AiApiKeys.FindAsync(id);

        if (key == null)
            return NotFound();

        dbContext.AiApiKeys.Remove(key);
        await dbContext.SaveChangesAsync();

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiApiKeyDeleted,
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.AiApiKey,
            EntityId = key.Id,
            Message = $"AI API key '{key.Name}' was deleted",
            Metadata = new
            {
                key.Name,
                key.ProviderName,
                key.Description,
                key.IsActive
            }
        });

        return NoContent();
    }

    #endregion


}
