using Automaton.Studio.Server.Entities;
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
    private readonly AiCodeGenerationService aiCodeGenerationService;
    private readonly ILogger<AiApiKeysController> logger;

    public AiApiKeysController(
        AiApiKeyService aiApiKeyService,
        AiCodeGenerationService aiCodeGenerationService,
        ILogger<AiApiKeysController> logger)
    {
        this.aiApiKeyService = aiApiKeyService;
        this.aiCodeGenerationService = aiCodeGenerationService;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AiApiKeyResponse>>> GetAiApiKeys()
    {
        var userId = GetUserId();
        var apiKeys = await aiApiKeyService.GetUserAiApiKeysAsync(userId);
        
        var response = apiKeys.Select(ak => new AiApiKeyResponse
        {
            Id = ak.Id,
            Name = ak.Name,
            Provider = ak.Provider,
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
            request.Provider, 
            request.ApiKey,
            request.Description);

        var response = new AiApiKeyResponse
        {
            Id = aiApiKey.Id,
            Name = aiApiKey.Name,
            Provider = aiApiKey.Provider,
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
            Provider = aiApiKey.Provider,
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

    [HttpPost("generate-code")]
    public async Task<ActionResult<CodeGenerationResponse>> GenerateCode([FromBody] CodeGenerationRequest request)
    {
        var userId = GetUserId();
        var result = await aiCodeGenerationService.GenerateCodeAsync(userId, request);

        if (result.IsSuccess)
        {
            return Ok(new CodeGenerationResponse 
            { 
                GeneratedCode = result.GeneratedCode,
                IsSuccess = true 
            });
        }
        else
        {
            return BadRequest(new CodeGenerationResponse 
            { 
                ErrorMessage = result.ErrorMessage,
                IsSuccess = false 
            });
        }
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
    public AiProvider Provider { get; set; }
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
    public AiProvider Provider { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
}

public class CodeGenerationResponse
{
    public bool IsSuccess { get; set; }
    public string GeneratedCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}