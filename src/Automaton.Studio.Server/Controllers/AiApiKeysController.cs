using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AiApiKeysController : ControllerBase
{
    private readonly AiApiKeyService aiApiKeyService;
    private readonly ILogger<AiApiKeysController> logger;

    public AiApiKeysController(
        AiApiKeyService aiApiKeyService,
        ILogger<AiApiKeysController> logger)
    {
        this.aiApiKeyService = aiApiKeyService;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AiApiKeyResponse>>> GetAiApiKeys([FromQuery] Guid? projectId = null)
    {
        var userId = GetUserId();
        var apiKeys = await aiApiKeyService.GetUserAiApiKeysAsync(userId, projectId);
        
        var response = apiKeys.Select(ak => new AiApiKeyResponse
        {
            Id = ak.Id,
            Name = ak.Name,
            ProviderName = ak.ProviderName,
            Description = ak.Description,
            CreatedAt = ak.CreatedAt,
            LastUsedAt = ak.LastUsedAt,
            IsActive = ak.IsActive
        });

        return Ok(response);
    }

    [HttpGet("providers")]
    public async Task<ActionResult<IEnumerable<AiProviderInfo>>> GetAvailableProviders([FromQuery] bool includeEnvironment = false)
    {
        var userId = GetUserId();
        var providers = await aiApiKeyService.GetAvailableProvidersAsync(userId, includeEnvironment);
        return Ok(providers);
    }

    [HttpPost]
    public async Task<ActionResult<AiApiKeyResponse>> CreateAiApiKey([FromBody] CreateAiApiKeyRequest request)
    {
        var userId = GetUserId();

        var aiApiKey = await aiApiKeyService.CreateAiApiKeyAsync(
            userId,
            request.Name,
            request.ProviderName,
            request.ApiKey,
            request.Description);

        var response = new AiApiKeyResponse
        {
            Id = aiApiKey.Id,
            Name = aiApiKey.Name,
            ProviderName = aiApiKey.ProviderName,
            Description = aiApiKey.Description,
            CreatedAt = aiApiKey.CreatedAt,
            LastUsedAt = aiApiKey.LastUsedAt,
            IsActive = aiApiKey.IsActive
        };

        return CreatedAtAction(nameof(GetAiApiKey), new { id = aiApiKey.Id }, response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AiApiKeyResponse>> GetAiApiKey(Guid id)
    {
        var userId = GetUserId();
        var aiApiKey = await aiApiKeyService.GetAiApiKeyAsync(id, userId);

        if (aiApiKey == null)
            return NotFound();

        var response = new AiApiKeyResponse
        {
            Id = aiApiKey.Id,
            Name = aiApiKey.Name,
            ProviderName = aiApiKey.ProviderName,
            Description = aiApiKey.Description,
            CreatedAt = aiApiKey.CreatedAt,
            LastUsedAt = aiApiKey.LastUsedAt,
            IsActive = aiApiKey.IsActive
        };

        return Ok(response);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAiApiKey(Guid id, [FromBody] UpdateAiApiKeyRequest request)
    {
        var userId = GetUserId();
        var success = await aiApiKeyService.UpdateAiApiKeyAsync(id, userId, request.Name, request.Description);
        
        if (!success)
            return NotFound();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAiApiKey(Guid id)
    {
        var userId = GetUserId();
        var success = await aiApiKeyService.DeleteAiApiKeyAsync(id, userId);
        
        if (!success)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Get decrypted API key for frontend use (MCP integration)
    /// </summary>
    [HttpPost("{id}/decrypt")]
    public async Task<ActionResult<DecryptedApiKeyResponse>> GetDecryptedApiKey(Guid id)
    {
        var userId = GetUserId();
        var decryptedKey = await aiApiKeyService.GetDecryptedApiKeyAsync(id, userId);
        
        if (string.IsNullOrEmpty(decryptedKey))
            return NotFound("API key not found or could not be decrypted");

        return Ok(new DecryptedApiKeyResponse 
        { 
            ApiKey = decryptedKey 
        });
    }

    /// <summary>
    /// Get environment API keys for desktop scenarios
    /// </summary>
    [HttpGet("environment")]
    public ActionResult<Dictionary<string, string?>> GetEnvironmentApiKeys()
    {
        var environmentKeys = aiApiKeyService.GetEnvironmentApiKeys();

        // Filter out null/empty values for security
        var filteredKeys = environmentKeys
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return Ok(filteredKeys);
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
public class CreateAiApiKeyRequest
{
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateAiApiKeyRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class AiApiKeyResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
}

public class DecryptedApiKeyResponse
{
    public string ApiKey { get; set; } = string.Empty;
}