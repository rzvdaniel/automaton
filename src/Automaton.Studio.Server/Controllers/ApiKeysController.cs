using Automaton.Core.Enums;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ApiKeysController : BaseController
{
    private readonly ApiKeyService apiKeyService;
    private readonly UserContextService userContextService;

    public ApiKeysController(ApiKeyService apiKeyService, UserContextService userContextService)
    {
        this.apiKeyService = apiKeyService;
        this.userContextService = userContextService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request)
    {
        var userId = userContextService.GetUserId();
        
        var (apiKey, entity) = await apiKeyService.CreateApiKeyAsync(
            userId, 
            request.Name, 
            request.Description, 
            request.ExpiresAt,
            request.Scope);

        return Ok(new CreateApiKeyResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            ApiKey = apiKey, // Only returned once during creation
            KeyPrefix = entity.KeyPrefix,
            Scope = entity.Scope,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetApiKeys()
    {
        var userId = userContextService.GetUserId();
        var apiKeys = await apiKeyService.GetUserApiKeysAsync(userId);

        var response = apiKeys.Select(ak => new ApiKeyResponse
        {
            Id = ak.Id,
            Name = ak.Name,
            KeyPrefix = ak.KeyPrefix,
            Scope = ak.Scope,
            CreatedAt = ak.CreatedAt,
            LastUsedAt = ak.LastUsedAt,
            ExpiresAt = ak.ExpiresAt,
            IsActive = ak.IsActive,
            Description = ak.Description
        });

        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> RevokeApiKey(Guid id)
    {
        var userId = userContextService.GetUserId();
        var success = await apiKeyService.RevokeApiKeyAsync(id, userId);

        if (!success)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Validates an API key and returns authentication status - used by console runners
    /// Anonymous endpoint - console runners test keys before using them for authentication
    /// </summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateApiKey([FromBody] ValidateApiKeyRequest request)
    {
        if (string.IsNullOrEmpty(request.ApiKey))
        {
            return BadRequest(new { message = "API key is required" });
        }

        var apiKeyEntity = await apiKeyService.ValidateApiKeyAsync(request.ApiKey);
        
        if (apiKeyEntity == null)
        {
            return Unauthorized(new { message = "Invalid or expired API key" });
        }

        // Check if the API key has appropriate scope for runner operations
        if (apiKeyEntity.Scope == ApiKeyScope.ReadOnly)
        {
            return Forbid("API key does not have sufficient permissions for runner operations. Required: Runner, Execute, or FullAccess scope.");
        }

        return Ok(new ValidateApiKeyResponse
        {
            IsValid = true,
            UserId = apiKeyEntity.UserId,
            UserName = apiKeyEntity.User.UserName ?? apiKeyEntity.User.Email,
            Scope = apiKeyEntity.Scope,
            KeyPrefix = apiKeyEntity.KeyPrefix
        });
    }
}

public class CreateApiKeyRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.FullAccess;
}

public class CreateApiKeyResponse
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string ApiKey { get; set; } // Only shown during creation
    public required string KeyPrefix { get; set; }
    public ApiKeyScope Scope { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ApiKeyResponse
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string KeyPrefix { get; set; }
    public ApiKeyScope Scope { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
}

public class ValidateApiKeyRequest
{
    public required string ApiKey { get; set; }
}

public class ValidateApiKeyResponse
{
    public bool IsValid { get; set; }
    public Guid UserId { get; set; }
    public required string UserName { get; set; }
    public ApiKeyScope Scope { get; set; }
    public required string KeyPrefix { get; set; }
}