using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Automaton.Studio.Server.Controllers.Admin;

/// <summary>
/// Admin controller for managing AI providers and models.
/// </summary>
[ApiController]
[Route("api/admin/ai-providers")]
[Authorize(Policy = "Administrators")]
public class AdminAiProvidersController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly ILogger<AdminAiProvidersController> logger;
    private readonly AiApiKeyService aiApiKeyService;

    public AdminAiProvidersController(
        ApplicationDbContext dbContext,
        ILogger<AdminAiProvidersController> logger,
        AiApiKeyService aiApiKeyService)
    {
        this.dbContext = dbContext;
        this.logger = logger;
        this.aiApiKeyService = aiApiKeyService;
    }

    #region Providers

    /// <summary>
    /// Get all AI providers with their model counts
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AiProviderAdminResponse>>> GetProviders()
    {
        var providers = await dbContext.AiProviders
            .Include(p => p.Models)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .Select(p => new AiProviderAdminResponse
            {
                Id = p.Id,
                Name = p.Name,
                DisplayName = p.DisplayName,
                Description = p.Description,
                BaseUrl = p.BaseUrl,
                IsActive = p.IsActive,
                RequiresApiKey = p.RequiresApiKey,
                EnvironmentKeyName = p.EnvironmentKeyName,
                ApiKeyHelpUrl = p.ApiKeyHelpUrl,
                IconName = p.IconName,
                SortOrder = p.SortOrder,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                ModelCount = p.Models.Count,
                ActiveModelCount = p.Models.Count(m => m.IsActive)
            })
            .ToListAsync();

        return Ok(providers);
    }

    /// <summary>
    /// Get a single AI provider by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AiProviderAdminResponse>> GetProvider(Guid id)
    {
        var provider = await dbContext.AiProviders
            .Include(p => p.Models)
            .Where(p => p.Id == id)
            .Select(p => new AiProviderAdminResponse
            {
                Id = p.Id,
                Name = p.Name,
                DisplayName = p.DisplayName,
                Description = p.Description,
                BaseUrl = p.BaseUrl,
                IsActive = p.IsActive,
                RequiresApiKey = p.RequiresApiKey,
                EnvironmentKeyName = p.EnvironmentKeyName,
                ApiKeyHelpUrl = p.ApiKeyHelpUrl,
                IconName = p.IconName,
                SortOrder = p.SortOrder,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                ModelCount = p.Models.Count,
                ActiveModelCount = p.Models.Count(m => m.IsActive)
            })
            .FirstOrDefaultAsync();

        if (provider == null)
            return NotFound();

        return Ok(provider);
    }

    /// <summary>
    /// Create a new AI provider
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AiProviderAdminResponse>> CreateProvider([FromBody] AdminCreateAiProviderRequest request)
    {
        // Check for duplicate name
        var existingProvider = await dbContext.AiProviders
            .FirstOrDefaultAsync(p => p.Name == request.Name);

        if (existingProvider != null)
            return BadRequest($"A provider with name '{request.Name}' already exists.");

        var provider = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Description = request.Description,
            BaseUrl = request.BaseUrl,
            IsActive = request.IsActive,
            RequiresApiKey = request.RequiresApiKey,
            EnvironmentKeyName = request.EnvironmentKeyName,
            ApiKeyHelpUrl = request.ApiKeyHelpUrl,
            IconName = request.IconName,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiProviders.Add(provider);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI provider {ProviderName} created by admin", provider.Name);

        return CreatedAtAction(nameof(GetProvider), new { id = provider.Id }, new AiProviderAdminResponse
        {
            Id = provider.Id,
            Name = provider.Name,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            BaseUrl = provider.BaseUrl,
            IsActive = provider.IsActive,
            RequiresApiKey = provider.RequiresApiKey,
            EnvironmentKeyName = provider.EnvironmentKeyName,
            ApiKeyHelpUrl = provider.ApiKeyHelpUrl,
            IconName = provider.IconName,
            SortOrder = provider.SortOrder,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt,
            ModelCount = 0,
            ActiveModelCount = 0
        });
    }

    /// <summary>
    /// Update an existing AI provider
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProvider(Guid id, [FromBody] AdminUpdateAiProviderRequest request)
    {
        var provider = await dbContext.AiProviders.FindAsync(id);

        if (provider == null)
            return NotFound();

        // Check for duplicate name if name is being changed
        if (request.Name != null && request.Name != provider.Name)
        {
            var existingProvider = await dbContext.AiProviders
                .FirstOrDefaultAsync(p => p.Name == request.Name && p.Id != id);

            if (existingProvider != null)
                return BadRequest($"A provider with name '{request.Name}' already exists.");

            provider.Name = request.Name;
        }

        if (request.DisplayName != null) provider.DisplayName = request.DisplayName;
        if (request.Description != null) provider.Description = request.Description;
        if (request.BaseUrl != null) provider.BaseUrl = request.BaseUrl;
        if (request.IsActive.HasValue) provider.IsActive = request.IsActive.Value;
        if (request.RequiresApiKey.HasValue) provider.RequiresApiKey = request.RequiresApiKey.Value;
        if (request.EnvironmentKeyName != null) provider.EnvironmentKeyName = request.EnvironmentKeyName;
        if (request.ApiKeyHelpUrl != null) provider.ApiKeyHelpUrl = request.ApiKeyHelpUrl;
        if (request.IconName != null) provider.IconName = request.IconName;
        if (request.SortOrder.HasValue) provider.SortOrder = request.SortOrder.Value;

        provider.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI provider {ProviderName} updated by admin", provider.Name);

        return NoContent();
    }

    /// <summary>
    /// Delete an AI provider and all its models
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProvider(Guid id)
    {
        var provider = await dbContext.AiProviders
            .Include(p => p.Models)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (provider == null)
            return NotFound();

        // Check if any API keys reference this provider
        var hasApiKeys = await dbContext.AiApiKeys
            .AnyAsync(k => k.AiProviderId == id);

        if (hasApiKeys)
            return BadRequest("Cannot delete provider with existing API keys. Deactivate it instead.");

        dbContext.AiProviders.Remove(provider);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI provider {ProviderName} deleted by admin", provider.Name);

        return NoContent();
    }

    #endregion

    #region Models

    /// <summary>
    /// Get all models for a provider
    /// </summary>
    [HttpGet("{providerId}/models")]
    public async Task<ActionResult<IEnumerable<AiModelAdminResponse>>> GetModels(Guid providerId)
    {
        var provider = await dbContext.AiProviders.FindAsync(providerId);
        if (provider == null)
            return NotFound("Provider not found");

        var models = await dbContext.AiModels
            .Where(m => m.AiProviderId == providerId)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.ModelId)
            .Select(m => new AiModelAdminResponse
            {
                Id = m.Id,
                AiProviderId = m.AiProviderId,
                ProviderName = m.Provider.Name,
                ModelId = m.ModelId,
                DisplayName = m.DisplayName,
                Description = m.Description,
                IsActive = m.IsActive,
                IsDefault = m.IsDefault,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                CostPer1MTokensInput = m.CostPer1MTokensInput,
                CostPer1MTokensOutput = m.CostPer1MTokensOutput,
                SortOrder = m.SortOrder,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt
            })
            .ToListAsync();

        return Ok(models);
    }

    /// <summary>
    /// Get all models across all providers
    /// </summary>
    [HttpGet("~/api/admin/ai-models")]
    public async Task<ActionResult<IEnumerable<AiModelAdminResponse>>> GetAllModels()
    {
        var models = await dbContext.AiModels
            .Include(m => m.Provider)
            .OrderBy(m => m.Provider.SortOrder)
            .ThenBy(m => m.SortOrder)
            .ThenBy(m => m.ModelId)
            .Select(m => new AiModelAdminResponse
            {
                Id = m.Id,
                AiProviderId = m.AiProviderId,
                ProviderName = m.Provider.Name,
                ModelId = m.ModelId,
                DisplayName = m.DisplayName,
                Description = m.Description,
                IsActive = m.IsActive,
                IsDefault = m.IsDefault,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                CostPer1MTokensInput = m.CostPer1MTokensInput,
                CostPer1MTokensOutput = m.CostPer1MTokensOutput,
                SortOrder = m.SortOrder,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt
            })
            .ToListAsync();

        return Ok(models);
    }

    /// <summary>
    /// Create a new model for a provider
    /// </summary>
    [HttpPost("{providerId}/models")]
    public async Task<ActionResult<AiModelAdminResponse>> CreateModel(Guid providerId, [FromBody] AdminCreateAiModelRequest request)
    {
        var provider = await dbContext.AiProviders.FindAsync(providerId);
        if (provider == null)
            return NotFound("Provider not found");

        // Check for duplicate model ID within the provider
        var existingModel = await dbContext.AiModels
            .FirstOrDefaultAsync(m => m.AiProviderId == providerId && m.ModelId == request.ModelId);

        if (existingModel != null)
            return BadRequest($"A model with ID '{request.ModelId}' already exists for this provider.");

        // If this model is set as default, unset other defaults for this provider
        if (request.IsDefault)
        {
            var existingDefaults = await dbContext.AiModels
                .Where(m => m.AiProviderId == providerId && m.IsDefault)
                .ToListAsync();

            foreach (var model in existingDefaults)
            {
                model.IsDefault = false;
            }
        }

        var newModel = new AiModel
        {
            Id = Guid.NewGuid(),
            AiProviderId = providerId,
            ModelId = request.ModelId,
            DisplayName = request.DisplayName,
            Description = request.Description,
            IsActive = request.IsActive,
            IsDefault = request.IsDefault,
            MaxTokens = request.MaxTokens,
            SupportsStreaming = request.SupportsStreaming,
            SupportsTools = request.SupportsTools,
            SupportsVision = request.SupportsVision,
            CostPer1MTokensInput = request.CostPer1MTokensInput,
            CostPer1MTokensOutput = request.CostPer1MTokensOutput,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiModels.Add(newModel);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI model {ModelId} created for provider {ProviderName} by admin", newModel.ModelId, provider.Name);

        return CreatedAtAction(nameof(GetModels), new { providerId }, new AiModelAdminResponse
        {
            Id = newModel.Id,
            AiProviderId = newModel.AiProviderId,
            ProviderName = provider.Name,
            ModelId = newModel.ModelId,
            DisplayName = newModel.DisplayName,
            Description = newModel.Description,
            IsActive = newModel.IsActive,
            IsDefault = newModel.IsDefault,
            MaxTokens = newModel.MaxTokens,
            SupportsStreaming = newModel.SupportsStreaming,
            SupportsTools = newModel.SupportsTools,
            SupportsVision = newModel.SupportsVision,
            CostPer1MTokensInput = newModel.CostPer1MTokensInput,
            CostPer1MTokensOutput = newModel.CostPer1MTokensOutput,
            SortOrder = newModel.SortOrder,
            CreatedAt = newModel.CreatedAt,
            UpdatedAt = newModel.UpdatedAt
        });
    }

    /// <summary>
    /// Update an existing model
    /// </summary>
    [HttpPut("~/api/admin/ai-models/{id}")]
    public async Task<IActionResult> UpdateModel(Guid id, [FromBody] AdminUpdateAiModelRequest request)
    {
        var model = await dbContext.AiModels.FindAsync(id);

        if (model == null)
            return NotFound();

        // Check for duplicate model ID if it's being changed
        if (request.ModelId != null && request.ModelId != model.ModelId)
        {
            var existingModel = await dbContext.AiModels
                .FirstOrDefaultAsync(m => m.AiProviderId == model.AiProviderId && m.ModelId == request.ModelId && m.Id != id);

            if (existingModel != null)
                return BadRequest($"A model with ID '{request.ModelId}' already exists for this provider.");

            model.ModelId = request.ModelId;
        }

        // If setting as default, unset other defaults for this provider
        if (request.IsDefault == true && !model.IsDefault)
        {
            var existingDefaults = await dbContext.AiModels
                .Where(m => m.AiProviderId == model.AiProviderId && m.IsDefault && m.Id != id)
                .ToListAsync();

            foreach (var defaultModel in existingDefaults)
            {
                defaultModel.IsDefault = false;
            }
        }

        if (request.DisplayName != null) model.DisplayName = request.DisplayName;
        if (request.Description != null) model.Description = request.Description;
        if (request.IsActive.HasValue) model.IsActive = request.IsActive.Value;
        if (request.IsDefault.HasValue) model.IsDefault = request.IsDefault.Value;
        if (request.MaxTokens.HasValue) model.MaxTokens = request.MaxTokens.Value;
        if (request.SupportsStreaming.HasValue) model.SupportsStreaming = request.SupportsStreaming.Value;
        if (request.SupportsTools.HasValue) model.SupportsTools = request.SupportsTools.Value;
        if (request.SupportsVision.HasValue) model.SupportsVision = request.SupportsVision.Value;
        if (request.CostPer1MTokensInput.HasValue) model.CostPer1MTokensInput = request.CostPer1MTokensInput.Value;
        if (request.CostPer1MTokensOutput.HasValue) model.CostPer1MTokensOutput = request.CostPer1MTokensOutput.Value;
        if (request.SortOrder.HasValue) model.SortOrder = request.SortOrder.Value;

        model.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI model {ModelId} updated by admin", model.ModelId);

        return NoContent();
    }

    /// <summary>
    /// Delete a model
    /// </summary>
    [HttpDelete("~/api/admin/ai-models/{id}")]
    public async Task<IActionResult> DeleteModel(Guid id)
    {
        var model = await dbContext.AiModels.FindAsync(id);

        if (model == null)
            return NotFound();

        dbContext.AiModels.Remove(model);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("AI model {ModelId} deleted by admin", model.ModelId);

        return NoContent();
    }

    #endregion

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
            .Where(k => k.AiProviderId == providerId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new AiApiKeyAdminResponse
            {
                Id = k.Id,
                Name = k.Name,
                ProviderName = k.ProviderName,
                Description = k.Description,
                CreatedAt = k.CreatedAt,
                LastUsedAt = k.LastUsedAt,
                IsActive = k.IsActive
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
        var provider = await dbContext.AiProviders.FindAsync(providerId);
        if (provider == null)
            return NotFound("Provider not found");

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required");

        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return BadRequest("API key is required");

        var entity = await aiApiKeyService.CreateAiApiKeyAsync(
            userId: Guid.Empty,
            name: request.Name,
            providerName: provider.Name,
            apiKey: request.ApiKey,
            description: request.Description);

        return CreatedAtAction(nameof(GetProviderKeys), new { providerId }, new AiApiKeyAdminResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            ProviderName = entity.ProviderName,
            Description = entity.Description,
            CreatedAt = entity.CreatedAt,
            LastUsedAt = entity.LastUsedAt,
            IsActive = entity.IsActive
        });
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

        logger.LogInformation("AI API key {KeyId} updated by admin", id);

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

        logger.LogInformation("AI API key {KeyId} deleted by admin", id);

        return NoContent();
    }

    #endregion
}

#region DTOs

public class AiProviderAdminResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsActive { get; set; }
    public bool RequiresApiKey { get; set; }
    public string? EnvironmentKeyName { get; set; }
    public string? ApiKeyHelpUrl { get; set; }
    public string? IconName { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int ModelCount { get; set; }
    public int ActiveModelCount { get; set; }
}

public class AdminCreateAiProviderRequest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool RequiresApiKey { get; set; } = true;
    public string? EnvironmentKeyName { get; set; }
    public string? ApiKeyHelpUrl { get; set; }
    public string? IconName { get; set; }
    public int SortOrder { get; set; } = 0;
}

public class AdminUpdateAiProviderRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? BaseUrl { get; set; }
    public bool? IsActive { get; set; }
    public bool? RequiresApiKey { get; set; }
    public string? EnvironmentKeyName { get; set; }
    public string? ApiKeyHelpUrl { get; set; }
    public string? IconName { get; set; }
    public int? SortOrder { get; set; }
}

public class AiModelAdminResponse
{
    public Guid Id { get; set; }
    public Guid AiProviderId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public int MaxTokens { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsTools { get; set; }
    public bool SupportsVision { get; set; }
    public decimal? CostPer1MTokensInput { get; set; }
    public decimal? CostPer1MTokensOutput { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AdminCreateAiModelRequest
{
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public int MaxTokens { get; set; } = 4096;
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsTools { get; set; } = false;
    public bool SupportsVision { get; set; } = false;
    public decimal? CostPer1MTokensInput { get; set; }
    public decimal? CostPer1MTokensOutput { get; set; }
    public int SortOrder { get; set; } = 0;
}

public class AdminUpdateAiModelRequest
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
    public decimal? CostPer1MTokensInput { get; set; }
    public decimal? CostPer1MTokensOutput { get; set; }
    public int? SortOrder { get; set; }
}

public class AiApiKeyAdminResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
}

public class AdminCreateAiApiKeyRequest
{
    public string Name { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AdminUpdateAiApiKeyRequest
{
    public string? Name { get; set; }
    public string? ApiKey { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
}

#endregion
