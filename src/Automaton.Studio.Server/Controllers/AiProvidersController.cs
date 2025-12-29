using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Admin-only controller for managing AI providers
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(UserRole.Administrators))]
public class AiProvidersController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<AiProvidersController> logger;

    public AiProvidersController(
        ApplicationDbContext dbContext,
        ILogger<AiProvidersController> logger)
    {
        this.dbContext = dbContext;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AiProviderResponse>>> GetAll([FromQuery] bool includeModels = false)
    {
        var query = dbContext.AiProviders.AsQueryable();

        if (includeModels)
        {
            query = query.Include(p => p.Models.Where(m => m.IsActive).OrderBy(m => m.SortOrder));
        }

        var providers = await query
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .ToListAsync();

        var response = providers.Select(p => new AiProviderResponse
        {
            Id = p.Id,
            Name = p.Name,
            DisplayName = p.DisplayName,
            Description = p.Description,
            BaseUrl = p.BaseUrl,
            IsActive = p.IsActive,
            RequiresApiKey = p.RequiresApiKey,
            EnvironmentKeyName = p.EnvironmentKeyName,
            SortOrder = p.SortOrder,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            Models = includeModels ? p.Models.Select(m => new AiModelResponse
            {
                Id = m.Id,
                ModelId = m.ModelId,
                DisplayName = m.DisplayName,
                Description = m.Description,
                IsActive = m.IsActive,
                IsDefault = m.IsDefault,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                CostPer1kTokensInput = m.CostPer1kTokensInput,
                CostPer1kTokensOutput = m.CostPer1kTokensOutput,
                SortOrder = m.SortOrder
            }).ToList() : null
        });

        return Ok(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AiProviderResponse>> Get(Guid id, [FromQuery] bool includeModels = false)
    {
        var query = dbContext.AiProviders.AsQueryable();

        if (includeModels)
        {
            query = query.Include(p => p.Models.OrderBy(m => m.SortOrder));
        }

        var provider = await query.FirstOrDefaultAsync(p => p.Id == id);

        if (provider == null)
            return NotFound();

        var response = new AiProviderResponse
        {
            Id = provider.Id,
            Name = provider.Name,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            BaseUrl = provider.BaseUrl,
            IsActive = provider.IsActive,
            RequiresApiKey = provider.RequiresApiKey,
            EnvironmentKeyName = provider.EnvironmentKeyName,
            SortOrder = provider.SortOrder,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt,
            Models = includeModels ? provider.Models.Select(m => new AiModelResponse
            {
                Id = m.Id,
                ModelId = m.ModelId,
                DisplayName = m.DisplayName,
                Description = m.Description,
                IsActive = m.IsActive,
                IsDefault = m.IsDefault,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                CostPer1kTokensInput = m.CostPer1kTokensInput,
                CostPer1kTokensOutput = m.CostPer1kTokensOutput,
                SortOrder = m.SortOrder
            }).ToList() : null
        };

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<AiProviderResponse>> Create([FromBody] CreateAiProviderRequest request)
    {
        // Check if provider with same name already exists
        if (await dbContext.AiProviders.AnyAsync(p => p.Name == request.Name))
        {
            return BadRequest(new { Error = $"Provider with name '{request.Name}' already exists" });
        }

        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            BaseUrl = request.BaseUrl,
            IsActive = request.IsActive ?? true,
            RequiresApiKey = request.RequiresApiKey ?? true,
            EnvironmentKeyName = request.EnvironmentKeyName,
            SortOrder = request.SortOrder ?? 0,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiProviders.Add(provider);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI provider {ProviderId} created by admin {UserId}", provider.Id, GetUserId());

        var response = new AiProviderResponse
        {
            Id = provider.Id,
            Name = provider.Name,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            BaseUrl = provider.BaseUrl,
            IsActive = provider.IsActive,
            RequiresApiKey = provider.RequiresApiKey,
            EnvironmentKeyName = provider.EnvironmentKeyName,
            SortOrder = provider.SortOrder,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt
        };

        return CreatedAtAction(nameof(Get), new { id = provider.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AiProviderResponse>> Update(Guid id, [FromBody] UpdateAiProviderRequest request)
    {
        var provider = await dbContext.AiProviders.FindAsync(id);

        if (provider == null)
            return NotFound();

        // Check if another provider has the same name
        if (request.Name != null && request.Name != provider.Name)
        {
            if (await dbContext.AiProviders.AnyAsync(p => p.Name == request.Name && p.Id != id))
            {
                return BadRequest(new { Error = $"Provider with name '{request.Name}' already exists" });
            }
            provider.Name = request.Name;
        }

        if (request.DisplayName != null) provider.DisplayName = request.DisplayName;
        if (request.Description != null) provider.Description = request.Description;
        if (request.BaseUrl != null) provider.BaseUrl = request.BaseUrl;
        if (request.IsActive.HasValue) provider.IsActive = request.IsActive.Value;
        if (request.RequiresApiKey.HasValue) provider.RequiresApiKey = request.RequiresApiKey.Value;
        if (request.EnvironmentKeyName != null) provider.EnvironmentKeyName = request.EnvironmentKeyName;
        if (request.SortOrder.HasValue) provider.SortOrder = request.SortOrder.Value;

        provider.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI provider {ProviderId} updated by admin {UserId}", provider.Id, GetUserId());

        var response = new AiProviderResponse
        {
            Id = provider.Id,
            Name = provider.Name,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            BaseUrl = provider.BaseUrl,
            IsActive = provider.IsActive,
            RequiresApiKey = provider.RequiresApiKey,
            EnvironmentKeyName = provider.EnvironmentKeyName,
            SortOrder = provider.SortOrder,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt
        };

        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var provider = await dbContext.AiProviders
            .Include(p => p.Models)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (provider == null)
            return NotFound();

        // Check if any models exist for this provider
        if (provider.Models.Any())
        {
            return BadRequest(new { Error = "Cannot delete provider with existing models. Delete models first or set provider to inactive." });
        }

        dbContext.AiProviders.Remove(provider);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI provider {ProviderId} deleted by admin {UserId}", id, GetUserId());

        return NoContent();
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }
}

// DTOs
public class AiProviderResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsActive { get; set; }
    public bool RequiresApiKey { get; set; }
    public string? EnvironmentKeyName { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<AiModelResponse>? Models { get; set; }
}

public class CreateAiProviderRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool? IsActive { get; set; }
    public bool? RequiresApiKey { get; set; }
    public string? EnvironmentKeyName { get; set; }
    public int? SortOrder { get; set; }
}

public class UpdateAiProviderRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool? IsActive { get; set; }
    public bool? RequiresApiKey { get; set; }
    public string? EnvironmentKeyName { get; set; }
    public int? SortOrder { get; set; }
}

public class AiModelResponse
{
    public Guid Id { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public int MaxTokens { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsTools { get; set; }
    public bool SupportsVision { get; set; }
    public decimal? CostPer1kTokensInput { get; set; }
    public decimal? CostPer1kTokensOutput { get; set; }
    public int SortOrder { get; set; }
}
