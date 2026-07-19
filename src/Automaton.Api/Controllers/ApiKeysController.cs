using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class ApiKeysController : BaseController
{
    internal static readonly IReadOnlySet<ApiKeyScope> UserCreatableScopes = new HashSet<ApiKeyScope>
    {
        ApiKeyScope.Developer,
        ApiKeyScope.ReadOnly,
        ApiKeyScope.WorkflowExecution,
        ApiKeyScope.WebApp
    };

    internal static readonly IReadOnlySet<ApiKeyScope> RunnerUsableScopes = new HashSet<ApiKeyScope>
    {
        ApiKeyScope.Runner,
        ApiKeyScope.FullAccess
    };

    private readonly ApiKeyService apiKeyService;
    private readonly UserContextService userContextService;
    private readonly ApplicationDbContext dbContext;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;

    public ApiKeysController(
        ApiKeyService apiKeyService,
        UserContextService userContextService,
        ApplicationDbContext dbContext,
        ConfirmedEmailGuardService confirmedEmailGuardService)
    {
        this.apiKeyService = apiKeyService;
        this.userContextService = userContextService;
        this.dbContext = dbContext;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request)
    {
        if (!UserCreatableScopes.Contains(request.Scope))
        {
            return BadRequest(new { message = "This API key type cannot be created manually." });
        }

        var userId = userContextService.GetUserId();
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        var (apiKey, entity) = await apiKeyService.CreateApiKeyAsync(
            userId,
            request.Name,
            request.Scope,
            request.Description,
            request.ExpiresAt);

        return Ok(new CreateApiKeyResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            ApiKey = apiKey,
            KeyPrefix = entity.KeyPrefix,
            Scope = entity.Scope,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetApiKeys([FromQuery] Guid? projectId = null)
    {
        var userId = userContextService.GetUserId();
        var apiKeys = await apiKeyService.GetUserApiKeysAsync(userId, projectId);

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
    public async Task<IActionResult> DeleteApiKey(Guid id)
    {
        var userId = userContextService.GetUserId();
        var success = await apiKeyService.DeleteApiKeyAsync(id, userId);

        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("validate")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.ApiKeyValidation)]
    public async Task<IActionResult> ValidateApiKey([FromBody] ValidateApiKeyRequest request)
    {
        if (string.IsNullOrEmpty(request.ApiKey))
        {
            return BadRequest(new { message = "API key is required" });
        }

        var apiKeyEntity = await apiKeyService.ValidateApiKeyAsync(request.ApiKey);

        if (apiKeyEntity == null || !RunnerUsableScopes.Contains(apiKeyEntity.Scope))
        {
            return Unauthorized(new { message = "Invalid Runner credential." });
        }

        var userId = await dbContext.ApiKeyUsers
            .Where(aku => aku.ApiKeyId == apiKeyEntity.Id)
            .Select(aku => aku.UserId)
            .FirstOrDefaultAsync();

        if (userId == Guid.Empty)
        {
            return Unauthorized(new { message = "API key has no associated user" });
        }

        var user = await dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return Unauthorized(new { message = "User not found for API key" });
        }

        return Ok(new ValidateApiKeyResponse
        {
            IsValid = true,
            UserId = userId,
            UserName = user.UserName ?? user.Email,
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
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.Developer;
}

public class CreateApiKeyResponse
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string ApiKey { get; set; }
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
