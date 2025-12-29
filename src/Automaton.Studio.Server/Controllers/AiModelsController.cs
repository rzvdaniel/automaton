using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Admin-only controller for managing AI models
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(UserRole.Administrators))]
public class AiModelsController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<AiModelsController> logger;

    public AiModelsController(
        ApplicationDbContext dbContext,
        ILogger<AiModelsController> logger)
    {
        this.dbContext = dbContext;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AiModelDetailResponse>>> GetAll([FromQuery] Guid? providerId = null, [FromQuery] bool? includeInactive = false)
    {
        var query = dbContext.AiModels.Include(m => m.Provider).AsQueryable();

        if (providerId.HasValue)
        {
            query = query.Where(m => m.AiProviderId == providerId.Value);
        }

        if (!includeInactive.GetValueOrDefault())
        {
            query = query.Where(m => m.IsActive);
        }

        var models = await query
            .OrderBy(m => m.Provider.SortOrder)
            .ThenBy(m => m.SortOrder)
            .ThenBy(m => m.DisplayName)
            .ToListAsync();

        var response = models.Select(m => new AiModelDetailResponse
        {
            Id = m.Id,
            ProviderId = m.AiProviderId,
            ProviderName = m.Provider.Name,
            ProviderDisplayName = m.Provider.DisplayName,
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
            SortOrder = m.SortOrder,
            CreatedAt = m.CreatedAt,
            UpdatedAt = m.UpdatedAt
        });

        return Ok(response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AiModelDetailResponse>> Get(Guid id)
    {
        var model = await dbContext.AiModels
            .Include(m => m.Provider)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (model == null)
            return NotFound();

        var response = new AiModelDetailResponse
        {
            Id = model.Id,
            ProviderId = model.AiProviderId,
            ProviderName = model.Provider.Name,
            ProviderDisplayName = model.Provider.DisplayName,
            ModelId = model.ModelId,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            IsDefault = model.IsDefault,
            MaxTokens = model.MaxTokens,
            SupportsStreaming = model.SupportsStreaming,
            SupportsTools = model.SupportsTools,
            SupportsVision = model.SupportsVision,
            CostPer1kTokensInput = model.CostPer1kTokensInput,
            CostPer1kTokensOutput = model.CostPer1kTokensOutput,
            SortOrder = model.SortOrder,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<AiModelDetailResponse>> Create([FromBody] CreateAiModelRequest request)
    {
        // Validate provider exists
        var provider = await dbContext.AiProviders.FindAsync(request.ProviderId);
        if (provider == null)
        {
            return BadRequest(new { Error = "Provider not found" });
        }

        // Check if model with same ModelId already exists for this provider
        if (await dbContext.AiModels.AnyAsync(m => m.AiProviderId == request.ProviderId && m.ModelId == request.ModelId))
        {
            return BadRequest(new { Error = $"Model '{request.ModelId}' already exists for provider '{provider.Name}'" });
        }

        var model = new AiModel
        {
            Id = Guid.NewGuid(),
            AiProviderId = request.ProviderId,
            ModelId = request.ModelId,
            DisplayName = request.DisplayName,
            Description = request.Description,
            IsActive = request.IsActive ?? true,
            IsDefault = request.IsDefault ?? false,
            MaxTokens = request.MaxTokens ?? 4096,
            SupportsStreaming = request.SupportsStreaming ?? true,
            SupportsTools = request.SupportsTools ?? false,
            SupportsVision = request.SupportsVision ?? false,
            CostPer1kTokensInput = request.CostPer1kTokensInput,
            CostPer1kTokensOutput = request.CostPer1kTokensOutput,
            SortOrder = request.SortOrder ?? 0,
            CreatedAt = DateTime.UtcNow
        };

        // If this model is set as default, unset others for this provider
        if (model.IsDefault)
        {
            var existingDefaults = await dbContext.AiModels
                .Where(m => m.AiProviderId == request.ProviderId && m.IsDefault)
                .ToListAsync();

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
        }

        dbContext.AiModels.Add(model);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI model {ModelId} created for provider {ProviderId} by admin {UserId}",
            model.Id, request.ProviderId, GetUserId());

        // Reload with provider to return full response
        model = await dbContext.AiModels
            .Include(m => m.Provider)
            .FirstAsync(m => m.Id == model.Id);

        var response = new AiModelDetailResponse
        {
            Id = model.Id,
            ProviderId = model.AiProviderId,
            ProviderName = model.Provider.Name,
            ProviderDisplayName = model.Provider.DisplayName,
            ModelId = model.ModelId,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            IsDefault = model.IsDefault,
            MaxTokens = model.MaxTokens,
            SupportsStreaming = model.SupportsStreaming,
            SupportsTools = model.SupportsTools,
            SupportsVision = model.SupportsVision,
            CostPer1kTokensInput = model.CostPer1kTokensInput,
            CostPer1kTokensOutput = model.CostPer1kTokensOutput,
            SortOrder = model.SortOrder,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        return CreatedAtAction(nameof(Get), new { id = model.Id }, response);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AiModelDetailResponse>> Update(Guid id, [FromBody] UpdateAiModelRequest request)
    {
        var model = await dbContext.AiModels
            .Include(m => m.Provider)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (model == null)
            return NotFound();

        // Check if another model has the same ModelId for this provider
        if (request.ModelId != null && request.ModelId != model.ModelId)
        {
            if (await dbContext.AiModels.AnyAsync(m => m.AiProviderId == model.AiProviderId && m.ModelId == request.ModelId && m.Id != id))
            {
                return BadRequest(new { Error = $"Model '{request.ModelId}' already exists for this provider" });
            }
            model.ModelId = request.ModelId;
        }

        if (request.DisplayName != null) model.DisplayName = request.DisplayName;
        if (request.Description != null) model.Description = request.Description;
        if (request.IsActive.HasValue) model.IsActive = request.IsActive.Value;
        if (request.MaxTokens.HasValue) model.MaxTokens = request.MaxTokens.Value;
        if (request.SupportsStreaming.HasValue) model.SupportsStreaming = request.SupportsStreaming.Value;
        if (request.SupportsTools.HasValue) model.SupportsTools = request.SupportsTools.Value;
        if (request.SupportsVision.HasValue) model.SupportsVision = request.SupportsVision.Value;
        if (request.CostPer1kTokensInput.HasValue) model.CostPer1kTokensInput = request.CostPer1kTokensInput.Value;
        if (request.CostPer1kTokensOutput.HasValue) model.CostPer1kTokensOutput = request.CostPer1kTokensOutput.Value;
        if (request.SortOrder.HasValue) model.SortOrder = request.SortOrder.Value;

        // Handle IsDefault - unset others if this is being set as default
        if (request.IsDefault.HasValue && request.IsDefault.Value && !model.IsDefault)
        {
            var existingDefaults = await dbContext.AiModels
                .Where(m => m.AiProviderId == model.AiProviderId && m.IsDefault && m.Id != id)
                .ToListAsync();

            foreach (var existing in existingDefaults)
            {
                existing.IsDefault = false;
            }
            model.IsDefault = true;
        }
        else if (request.IsDefault.HasValue && !request.IsDefault.Value)
        {
            model.IsDefault = false;
        }

        model.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI model {ModelId} updated by admin {UserId}", model.Id, GetUserId());

        var response = new AiModelDetailResponse
        {
            Id = model.Id,
            ProviderId = model.AiProviderId,
            ProviderName = model.Provider.Name,
            ProviderDisplayName = model.Provider.DisplayName,
            ModelId = model.ModelId,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            IsDefault = model.IsDefault,
            MaxTokens = model.MaxTokens,
            SupportsStreaming = model.SupportsStreaming,
            SupportsTools = model.SupportsTools,
            SupportsVision = model.SupportsVision,
            CostPer1kTokensInput = model.CostPer1kTokensInput,
            CostPer1kTokensOutput = model.CostPer1kTokensOutput,
            SortOrder = model.SortOrder,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };

        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(Guid id)
    {
        var model = await dbContext.AiModels.FindAsync(id);

        if (model == null)
            return NotFound();

        dbContext.AiModels.Remove(model);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI model {ModelId} deleted by admin {UserId}", id, GetUserId());

        return NoContent();
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }
}

// DTOs
public class AiModelDetailResponse
{
    public Guid Id { get; set; }
    public Guid ProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderDisplayName { get; set; } = string.Empty;
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
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateAiModelRequest
{
    public Guid ProviderId { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsDefault { get; set; }
    public int? MaxTokens { get; set; }
    public bool? SupportsStreaming { get; set; }
    public bool? SupportsTools { get; set; }
    public bool? SupportsVision { get; set; }
    public decimal? CostPer1kTokensInput { get; set; }
    public decimal? CostPer1kTokensOutput { get; set; }
    public int? SortOrder { get; set; }
}

public class UpdateAiModelRequest
{
    public string? ModelId { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsDefault { get; set; }
    public int? MaxTokens { get; set; }
    public bool? SupportsStreaming { get; set; }
    public bool? SupportsTools { get; set; }
    public bool? SupportsVision { get; set; }
    public decimal? CostPer1kTokensInput { get; set; }
    public decimal? CostPer1kTokensOutput { get; set; }
    public int? SortOrder { get; set; }
}
