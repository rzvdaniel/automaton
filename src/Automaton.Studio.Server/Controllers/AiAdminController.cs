using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Entities.Enums;
using Automaton.Studio.Server.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/admin/ai-providers")]
[Authorize(Roles = UserRole.Administrators)]
public class AiAdminController : BaseController
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AiAdminController> _logger;

    public AiAdminController(
        ApplicationDbContext dbContext,
        ILogger<AiAdminController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all AI provider configurations
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllProviders()
    {
        try
        {
            var providers = await _dbContext.AiProviderConfigs
                .Include(p => p.Models.Where(m => m.Enabled))
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.ProviderName)
                .ToListAsync();

            return Ok(providers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting AI provider configurations");
            return StatusCode(500, new { error = "Failed to retrieve AI provider configurations" });
        }
    }

    /// <summary>
    /// Get a specific AI provider configuration
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProvider(Guid id)
    {
        try
        {
            var provider = await _dbContext.AiProviderConfigs
                .Include(p => p.Models)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (provider == null)
                return NotFound(new { error = "Provider configuration not found" });

            return Ok(provider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting AI provider configuration {ProviderId}", id);
            return StatusCode(500, new { error = "Failed to retrieve AI provider configuration" });
        }
    }

    /// <summary>
    /// Create a new AI provider configuration
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateProvider([FromBody] CreateAiProviderRequest request)
    {
        try
        {
            var userId = GetUserId();

            // Check if provider already exists
            var existingProvider = await _dbContext.AiProviderConfigs
                .FirstOrDefaultAsync(p => p.Provider == request.Provider);

            if (existingProvider != null)
                return Conflict(new { error = "Provider configuration already exists" });

            var provider = new AiProvider
            {
                Id = Guid.NewGuid(),
                ProviderName = request.ProviderName,
                Provider = request.Provider,
                Description = request.Description,
                DefaultModel = request.DefaultModel,
                Enabled = request.Enabled,
                DisplayOrder = request.DisplayOrder,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };

            _dbContext.AiProviderConfigs.Add(provider);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("AI provider configuration created: {ProviderName} by user {UserId}", 
                request.ProviderName, userId);

            return CreatedAtAction(nameof(GetProvider), new { id = provider.Id }, provider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating AI provider configuration");
            return StatusCode(500, new { error = "Failed to create AI provider configuration" });
        }
    }

    /// <summary>
    /// Update an existing AI provider configuration
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProvider(Guid id, [FromBody] UpdateAiProviderRequest request)
    {
        try
        {
            var userId = GetUserId();
            var provider = await _dbContext.AiProviderConfigs.FindAsync(id);

            if (provider == null)
                return NotFound(new { error = "Provider configuration not found" });

            provider.ProviderName = request.ProviderName;
            provider.Description = request.Description;
            provider.DefaultModel = request.DefaultModel;
            provider.Enabled = request.Enabled;
            provider.DisplayOrder = request.DisplayOrder;
            provider.UpdatedAt = DateTime.UtcNow;
            provider.UpdatedBy = userId;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("AI provider configuration updated: {ProviderName} by user {UserId}", 
                request.ProviderName, userId);

            return Ok(provider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating AI provider configuration {ProviderId}", id);
            return StatusCode(500, new { error = "Failed to update AI provider configuration" });
        }
    }

    /// <summary>
    /// Delete an AI provider configuration
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProvider(Guid id)
    {
        try
        {
            var userId = GetUserId();
            var provider = await _dbContext.AiProviderConfigs
                .Include(p => p.Models)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (provider == null)
                return NotFound(new { error = "Provider configuration not found" });

            _dbContext.AiProviderConfigs.Remove(provider);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("AI provider configuration deleted: {ProviderName} by user {UserId}", 
                provider.ProviderName, userId);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting AI provider configuration {ProviderId}", id);
            return StatusCode(500, new { error = "Failed to delete AI provider configuration" });
        }
    }

    /// <summary>
    /// Create a new AI model configuration
    /// </summary>
    [HttpPost("{providerId}/models")]
    public async Task<IActionResult> CreateModel(Guid providerId, [FromBody] CreateAiModelRequest request)
    {
        try
        {
            var userId = GetUserId();

            // Check if provider exists
            var provider = await _dbContext.AiProviderConfigs.FindAsync(providerId);
            if (provider == null)
                return NotFound(new { error = "Provider configuration not found" });

            // Check if model already exists for this provider
            var existingModel = await _dbContext.AiModelConfigs
                .FirstOrDefaultAsync(m => m.ProviderId == providerId && m.ModelName == request.ModelName);

            if (existingModel != null)
                return Conflict(new { error = "Model configuration already exists for this provider" });

            var model = new AiModel
            {
                Id = Guid.NewGuid(),
                ProviderId = providerId,
                ModelName = request.ModelName,
                DisplayName = request.DisplayName,
                Description = request.Description,
                Enabled = request.Enabled,
                DisplayOrder = request.DisplayOrder,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };

            _dbContext.AiModelConfigs.Add(model);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("AI model configuration created: {ModelName} for provider {ProviderId} by user {UserId}", 
                request.ModelName, providerId, userId);

            return CreatedAtAction(nameof(GetModel), new { providerId, modelId = model.Id }, model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating AI model configuration for provider {ProviderId}", providerId);
            return StatusCode(500, new { error = "Failed to create AI model configuration" });
        }
    }

    /// <summary>
    /// Get a specific AI model configuration
    /// </summary>
    [HttpGet("{providerId}/models/{modelId}")]
    public async Task<IActionResult> GetModel(Guid providerId, Guid modelId)
    {
        try
        {
            var model = await _dbContext.AiModelConfigs
                .Include(m => m.Provider)
                .FirstOrDefaultAsync(m => m.Id == modelId && m.ProviderId == providerId);

            if (model == null)
                return NotFound(new { error = "Model configuration not found" });

            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting AI model configuration {ModelId}", modelId);
            return StatusCode(500, new { error = "Failed to retrieve AI model configuration" });
        }
    }

    /// <summary>
    /// Update an existing AI model configuration
    /// </summary>
    [HttpPut("{providerId}/models/{modelId}")]
    public async Task<IActionResult> UpdateModel(Guid providerId, Guid modelId, [FromBody] UpdateAiModelRequest request)
    {
        try
        {
            var userId = GetUserId();
            var model = await _dbContext.AiModelConfigs
                .FirstOrDefaultAsync(m => m.Id == modelId && m.ProviderId == providerId);

            if (model == null)
                return NotFound(new { error = "Model configuration not found" });

            model.ModelName = request.ModelName;
            model.DisplayName = request.DisplayName;
            model.Description = request.Description;
            model.Enabled = request.Enabled;
            model.DisplayOrder = request.DisplayOrder;
            model.UpdatedAt = DateTime.UtcNow;
            model.UpdatedBy = userId;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("AI model configuration updated: {ModelName} by user {UserId}", 
                request.ModelName, userId);

            return Ok(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating AI model configuration {ModelId}", modelId);
            return StatusCode(500, new { error = "Failed to update AI model configuration" });
        }
    }

    /// <summary>
    /// Delete an AI model configuration
    /// </summary>
    [HttpDelete("{providerId}/models/{modelId}")]
    public async Task<IActionResult> DeleteModel(Guid providerId, Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var model = await _dbContext.AiModelConfigs
                .FirstOrDefaultAsync(m => m.Id == modelId && m.ProviderId == providerId);

            if (model == null)
                return NotFound(new { error = "Model configuration not found" });

            _dbContext.AiModelConfigs.Remove(model);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("AI model configuration deleted: {ModelName} by user {UserId}", 
                model.ModelName, userId);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting AI model configuration {ModelId}", modelId);
            return StatusCode(500, new { error = "Failed to delete AI model configuration" });
        }
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}

// Request/Response models
public class CreateAiProviderRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public AiProviderType Provider { get; set; }
    public string? Description { get; set; }
    public string DefaultModel { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
}

public class UpdateAiProviderRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DefaultModel { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }
}

public class CreateAiModelRequest
{
    public string ModelName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
}

public class UpdateAiModelRequest
{
    public string ModelName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }
}
