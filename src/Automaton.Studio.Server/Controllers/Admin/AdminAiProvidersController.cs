using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Constants;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Interfaces;
using Automaton.Mcp.SystemPrompts;
using System.ComponentModel.DataAnnotations;
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
    private readonly IAiProvidersSettingsService settingsService;
    private readonly IAuditEventService auditEventService;

    public AdminAiProvidersController(
        ApplicationDbContext dbContext,
        ILogger<AdminAiProvidersController> logger,
        AiApiKeyService aiApiKeyService,
        IAiProvidersSettingsService settingsService,
        IAuditEventService auditEventService)
    {
        this.dbContext = dbContext;
        this.logger = logger;
        this.aiApiKeyService = aiApiKeyService;
        this.settingsService = settingsService;
        this.auditEventService = auditEventService;
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
                ExtraBodyJson = p.ExtraBodyJson,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                ModelCount = p.Models.Count,
                ActiveModelCount = p.Models.Count(m => m.IsActive),
                HasSystemKey = dbContext.AiApiKeys.Any(k => k.ProviderName == p.Name && k.IsActive && k.CreatedByUserId == null),
                HasDefaultModel = p.Models.Any(m => m.IsActive && m.IsDefault)
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
                ExtraBodyJson = p.ExtraBodyJson,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                ModelCount = p.Models.Count,
                ActiveModelCount = p.Models.Count(m => m.IsActive),
                HasSystemKey = dbContext.AiApiKeys.Any(k => k.ProviderName == p.Name && k.IsActive && k.CreatedByUserId == null),
                HasDefaultModel = p.Models.Any(m => m.IsActive && m.IsDefault)
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
            ExtraBodyJson = request.ExtraBodyJson,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiProviders.Add(provider);
        await dbContext.SaveChangesAsync();

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiProviderCreated,
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.AiProvider,
            EntityId = provider.Id,
            Message = $"AI provider '{provider.Name}' was created",
            Outcome = AuditOutcomes.Success,
            Metadata = new
            {
                provider.Name,
                provider.DisplayName,
                HasBaseUrl = !string.IsNullOrWhiteSpace(provider.BaseUrl),
                provider.IsActive,
                provider.RequiresApiKey,
                provider.EnvironmentKeyName,
                provider.ApiKeyHelpUrl,
                provider.IconName,
                provider.SortOrder,
                HasExtraBodyJson = !string.IsNullOrWhiteSpace(provider.ExtraBodyJson)
            }
        });

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
            ExtraBodyJson = provider.ExtraBodyJson,
            CreatedAt = provider.CreatedAt,
            UpdatedAt = provider.UpdatedAt,
            ModelCount = 0,
            ActiveModelCount = 0,
            HasSystemKey = false,
            HasDefaultModel = false
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

        var previousIsActive = provider.IsActive;
        if (request.DisplayName != null) provider.DisplayName = request.DisplayName;
        if (request.Description != null) provider.Description = request.Description;
        if (request.BaseUrl != null) provider.BaseUrl = request.BaseUrl;
        if (request.IsActive.HasValue) provider.IsActive = request.IsActive.Value;
        if (request.RequiresApiKey.HasValue) provider.RequiresApiKey = request.RequiresApiKey.Value;
        if (request.EnvironmentKeyName != null) provider.EnvironmentKeyName = request.EnvironmentKeyName;
        if (request.ApiKeyHelpUrl != null) provider.ApiKeyHelpUrl = request.ApiKeyHelpUrl;
        if (request.IconName != null) provider.IconName = request.IconName;
        if (request.SortOrder.HasValue) provider.SortOrder = request.SortOrder.Value;
        // Allow clearing ExtraBodyJson by sending empty string; null means "don't change"
        if (request.ExtraBodyJson != null) provider.ExtraBodyJson = string.IsNullOrWhiteSpace(request.ExtraBodyJson) ? null : request.ExtraBodyJson;

        provider.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        var isStatusChange = request.IsActive.HasValue && previousIsActive != provider.IsActive;
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = isStatusChange
                ? provider.IsActive
                    ? AuditEventTypes.AiProviderEnabled
                    : AuditEventTypes.AiProviderDisabled
                : AuditEventTypes.AiProviderUpdated,
            Action = isStatusChange
                ? provider.IsActive
                    ? AuditActions.Enabled
                    : AuditActions.Disabled
                : AuditActions.Updated,
            EntityType = AuditEntityTypes.AiProvider,
            EntityId = provider.Id,
            Message = isStatusChange
                ? provider.IsActive
                    ? $"AI provider '{provider.Name}' was enabled"
                    : $"AI provider '{provider.Name}' was disabled"
                : $"AI provider '{provider.Name}' was updated",
            Outcome = AuditOutcomes.Success,
            Metadata = new
            {
                provider.Name,
                provider.DisplayName,
                HasBaseUrl = !string.IsNullOrWhiteSpace(provider.BaseUrl),
                provider.IsActive,
                provider.RequiresApiKey,
                provider.EnvironmentKeyName,
                provider.ApiKeyHelpUrl,
                provider.IconName,
                provider.SortOrder,
                HasExtraBodyJson = !string.IsNullOrWhiteSpace(provider.ExtraBodyJson)
            }
        });

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

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiProviderDeleted,
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.AiProvider,
            EntityId = provider.Id,
            Message = $"AI provider '{provider.Name}' was deleted",
            Outcome = AuditOutcomes.Success,
            Metadata = new
            {
                provider.Name,
                provider.DisplayName,
                HasBaseUrl = !string.IsNullOrWhiteSpace(provider.BaseUrl),
                provider.IsActive,
                provider.RequiresApiKey,
                provider.EnvironmentKeyName,
                provider.ApiKeyHelpUrl,
                provider.IconName,
                provider.SortOrder,
                HasExtraBodyJson = !string.IsNullOrWhiteSpace(provider.ExtraBodyJson),
                ModelCount = provider.Models.Count
            }
        });

        return NoContent();
    }

    #endregion

    #region Models

    /// <summary>
    /// Get the current global default and planning models (across all providers)
    /// </summary>
    [HttpGet("~/api/admin/ai-models/global-defaults")]
    public async Task<ActionResult<GlobalAiDefaultsResponse>> GetGlobalDefaults()
    {
        var defaultModel = await dbContext.AiModels
            .Where(m => m.IsDefault)
            .Select(m => new AiModelAdminResponse
            {
                Id = m.Id,
                AiProviderId = m.AiProviderId,
                ProviderName = m.Provider.Name,
                ModelId = m.ModelId,
                DisplayName = m.DisplayName,
                IsDefault = m.IsDefault,
                IsPlanningModel = m.IsPlanningModel,
                IsActive = m.IsActive,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                SortOrder = m.SortOrder,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt
            })
            .FirstOrDefaultAsync();

        var planningModel = await dbContext.AiModels
            .Where(m => m.IsPlanningModel)
            .Select(m => new AiModelAdminResponse
            {
                Id = m.Id,
                AiProviderId = m.AiProviderId,
                ProviderName = m.Provider.Name,
                ModelId = m.ModelId,
                DisplayName = m.DisplayName,
                IsDefault = m.IsDefault,
                IsPlanningModel = m.IsPlanningModel,
                IsActive = m.IsActive,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                SortOrder = m.SortOrder,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt
            })
            .FirstOrDefaultAsync();

        return Ok(new GlobalAiDefaultsResponse
        {
            DefaultModel = defaultModel,
            PlanningModel = planningModel,
            DefaultMaxOutputTokens = (await settingsService.GetEffectiveConfigAsync()).DefaultMaxOutputTokens
        });
    }

    /// <summary>
    /// Get a single AI model by ID
    /// </summary>
    [HttpGet("~/api/admin/ai-models/{id}")]
    public async Task<ActionResult<AiModelAdminResponse>> GetModel(Guid id)
    {
        var model = await dbContext.AiModels
            .Where(m => m.Id == id)
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
                IsPlanningModel = m.IsPlanningModel,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                CostPer1MTokensInput = m.CostPer1MTokensInput,
                CostPer1MTokensOutput = m.CostPer1MTokensOutput,
                ExtraBodyJson = m.ExtraBodyJson,
                SortOrder = m.SortOrder,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt
            })
            .FirstOrDefaultAsync();

        if (model == null)
            return NotFound();

        return Ok(model);
    }

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
                IsPlanningModel = m.IsPlanningModel,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                CostPer1MTokensInput = m.CostPer1MTokensInput,
                CostPer1MTokensOutput = m.CostPer1MTokensOutput,
                ExtraBodyJson = m.ExtraBodyJson,
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
                IsPlanningModel = m.IsPlanningModel,
                MaxTokens = m.MaxTokens,
                SupportsStreaming = m.SupportsStreaming,
                SupportsTools = m.SupportsTools,
                SupportsVision = m.SupportsVision,
                CostPer1MTokensInput = m.CostPer1MTokensInput,
                CostPer1MTokensOutput = m.CostPer1MTokensOutput,
                ExtraBodyJson = m.ExtraBodyJson,
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

        // If this model is set as default, require a system API key for the provider
        if (request.IsDefault)
        {
            var hasSystemKey = await aiApiKeyService.HasSystemApiKeyAsync(provider.Name);

            if (!hasSystemKey)
                return BadRequest($"Cannot set '{provider.Name}' as the global default: no system API key exists for this provider. " +
                                  "Add an API key with 'For all users' scope first.");

            var existingDefaults = await dbContext.AiModels
                .Where(m => m.IsDefault)
                .ToListAsync();

            foreach (var model in existingDefaults)
            {
                model.IsDefault = false;
            }
        }

        // If this model is set as planning model, unset the global planning model across all providers
        if (request.IsPlanningModel)
        {
            var existingPlanners = await dbContext.AiModels
                .Where(m => m.IsPlanningModel)
                .ToListAsync();

            foreach (var model in existingPlanners)
            {
                model.IsPlanningModel = false;
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
            IsPlanningModel = request.IsPlanningModel,
            MaxTokens = request.MaxTokens,
            SupportsStreaming = request.SupportsStreaming,
            SupportsTools = request.SupportsTools,
            SupportsVision = request.SupportsVision,
            CostPer1MTokensInput = request.CostPer1MTokensInput,
            CostPer1MTokensOutput = request.CostPer1MTokensOutput,
            ExtraBodyJson = request.ExtraBodyJson,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AiModels.Add(newModel);
        await dbContext.SaveChangesAsync();

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiModelCreated,
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.AiModel,
            EntityId = newModel.Id,
            Message = $"AI model '{newModel.ModelId}' was created for provider '{provider.Name}'",
            Metadata = new
            {
                ProviderId = provider.Id,
                ProviderName = provider.Name,
                newModel.ModelId,
                newModel.DisplayName,
                newModel.IsActive,
                newModel.IsDefault,
                newModel.IsPlanningModel,
                newModel.MaxTokens,
                newModel.SupportsStreaming,
                newModel.SupportsTools,
                newModel.SupportsVision,
                HasExtraBodyJson = !string.IsNullOrWhiteSpace(newModel.ExtraBodyJson),
                newModel.SortOrder
            }
        });

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
            IsPlanningModel = newModel.IsPlanningModel,
            MaxTokens = newModel.MaxTokens,
            SupportsStreaming = newModel.SupportsStreaming,
            SupportsTools = newModel.SupportsTools,
            SupportsVision = newModel.SupportsVision,
            CostPer1MTokensInput = newModel.CostPer1MTokensInput,
            CostPer1MTokensOutput = newModel.CostPer1MTokensOutput,
            ExtraBodyJson = newModel.ExtraBodyJson,
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

        // If setting as default, require a system API key for the provider
        if (request.IsDefault == true && !model.IsDefault)
        {
            var provider = await dbContext.AiProviders.FindAsync(model.AiProviderId);
            var providerName = provider?.Name ?? string.Empty;

            var hasSystemKey = await aiApiKeyService.HasSystemApiKeyAsync(providerName);

            if (!hasSystemKey)
                return BadRequest($"Cannot set '{providerName}' as the global default: no system API key exists for this provider. " +
                                  "Add an API key with 'For all users' scope first.");

            var existingDefaults = await dbContext.AiModels
                .Where(m => m.IsDefault && m.Id != id)
                .ToListAsync();

            foreach (var defaultModel in existingDefaults)
            {
                defaultModel.IsDefault = false;
            }
        }

        // If setting as planning model, unset the global planning model across all providers
        if (request.IsPlanningModel == true && !model.IsPlanningModel)
        {
            var existingPlanners = await dbContext.AiModels
                .Where(m => m.IsPlanningModel && m.Id != id)
                .ToListAsync();

            foreach (var plannerModel in existingPlanners)
            {
                plannerModel.IsPlanningModel = false;
            }
        }

        if (request.DisplayName != null) model.DisplayName = request.DisplayName;
        if (request.Description != null) model.Description = request.Description;
        if (request.IsActive.HasValue) model.IsActive = request.IsActive.Value;
        if (request.IsDefault.HasValue) model.IsDefault = request.IsDefault.Value;
        if (request.IsPlanningModel.HasValue) model.IsPlanningModel = request.IsPlanningModel.Value;
        if (request.MaxTokens.HasValue) model.MaxTokens = request.MaxTokens.Value;
        if (request.SupportsStreaming.HasValue) model.SupportsStreaming = request.SupportsStreaming.Value;
        if (request.SupportsTools.HasValue) model.SupportsTools = request.SupportsTools.Value;
        if (request.SupportsVision.HasValue) model.SupportsVision = request.SupportsVision.Value;
        if (request.CostPer1MTokensInput.HasValue) model.CostPer1MTokensInput = request.CostPer1MTokensInput.Value;
        if (request.CostPer1MTokensOutput.HasValue) model.CostPer1MTokensOutput = request.CostPer1MTokensOutput.Value;
        if (request.ExtraBodyJson != null) model.ExtraBodyJson = string.IsNullOrWhiteSpace(request.ExtraBodyJson) ? null : request.ExtraBodyJson;
        if (request.SortOrder.HasValue) model.SortOrder = request.SortOrder.Value;

        model.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        var auditProviderName = await dbContext.AiProviders
            .Where(p => p.Id == model.AiProviderId)
            .Select(p => p.Name)
            .FirstOrDefaultAsync() ?? string.Empty;

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiModelUpdated,
            Action = AuditActions.Updated,
            EntityType = AuditEntityTypes.AiModel,
            EntityId = model.Id,
            Message = $"AI model '{model.ModelId}' was updated",
            Metadata = new
            {
                model.AiProviderId,
                ProviderName = auditProviderName,
                model.ModelId,
                model.DisplayName,
                model.IsActive,
                model.IsDefault,
                model.IsPlanningModel,
                model.MaxTokens,
                model.SupportsStreaming,
                model.SupportsTools,
                model.SupportsVision,
                HasExtraBodyJson = !string.IsNullOrWhiteSpace(model.ExtraBodyJson),
                model.SortOrder
            }
        });

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

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiModelDeleted,
            Action = AuditActions.Deleted,
            EntityType = AuditEntityTypes.AiModel,
            EntityId = model.Id,
            Message = $"AI model '{model.ModelId}' was deleted",
            Metadata = new
            {
                model.AiProviderId,
                model.ModelId,
                model.DisplayName,
                model.IsDefault,
                model.IsPlanningModel
            }
        });

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

        // If this is the only active system key across all providers, automatically set
        // the provider's preferred model as the global default so the system is usable immediately.
        var systemKeyCount = await dbContext.AiApiKeys
            .CountAsync(k => k.IsActive && k.CreatedByUserId == null);

        if (systemKeyCount == 1)
        {
            // Prefer a model that supports tool calling — reasoning-only models reject requests
            // with tools and will immediately 400 every conversation.
            var preferredModel = await dbContext.AiModels
                .Where(m => m.AiProviderId == providerId && m.IsActive)
                .OrderByDescending(m => m.SupportsTools)
                .ThenByDescending(m => m.IsDefault)
                .ThenBy(m => m.SortOrder)
                .FirstOrDefaultAsync();

            if (preferredModel != null && !preferredModel.IsDefault)
            {
                // Clear any existing global default first
                await dbContext.AiModels
                    .Where(m => m.IsDefault)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDefault, false));

                preferredModel.IsDefault = true;
                await dbContext.SaveChangesAsync();

                logger.LogDebug(
                    "Auto-promoted model '{ModelId}' of provider '{ProviderName}' as global default (first system key)",
                    preferredModel.ModelId, provider.Name);
            }
        }

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiApiKeyCreated,
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.AiApiKey,
            EntityId = entity.Id,
            Message = $"AI API key '{entity.Name}' was created for provider '{provider.Name}'",
            Metadata = new
            {
                entity.Name,
                entity.ProviderName,
                entity.Description,
                entity.IsActive,
                entity.IsSystemKey
            }
        });

        return CreatedAtAction(nameof(GetProviderKeys), new { providerId }, new AiApiKeyAdminResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            ProviderName = entity.ProviderName,
            Description = entity.Description,
            CreatedAt = entity.CreatedAt,
            LastUsedAt = entity.LastUsedAt,
            IsActive = entity.IsActive,
            IsSystemKey = entity.IsSystemKey
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

    #region Operational Settings

    /// <summary>
    /// Get the current operational AI settings (iteration limits, token caps, etc.).
    /// Returns the DB-persisted values if set, otherwise the appsettings.json defaults.
    /// </summary>
    [HttpGet("~/api/admin/ai-settings")]
    public async Task<ActionResult<AiProvidersOperationalConfigResponse>> GetAiSettings()
    {
        var settings = await settingsService.GetOperationalSettingsAsync();
        return Ok(MapToResponse(settings));
    }

    /// <summary>
    /// Save operational AI settings to the database.
    /// Changes take effect immediately — no server restart required.
    /// </summary>
    [HttpPut("~/api/admin/ai-settings")]
    public async Task<ActionResult<AiProvidersOperationalConfigResponse>> SaveAiSettings(
        [FromBody] SaveAiSettingsRequest request)
    {
        var normalizedHistoryCompaction = NormalizeHistoryCompaction(request.HistoryCompaction);

        var settings = new AiProvidersOperationalConfig
        {
            MaxConversationHistoryMessages = request.MaxConversationHistoryMessages,
            MaxIterations                  = request.MaxIterations,
            MaxConsecutiveFailures         = request.MaxConsecutiveFailures,
            DefaultMaxOutputTokens         = request.DefaultMaxOutputTokens,
            AppVerificationDefaultStrategy = request.AppVerificationDefaultStrategy,
            AppVerificationAllowUserOverride = request.AppVerificationAllowUserOverride,
            AppExecutionDefaultPolicy = request.AppExecutionDefaultPolicy,
            AppExecutionAllowUserOverride = request.AppExecutionAllowUserOverride,
            IterationLimits = new()
            {
                Simple     = request.IterationLimits.Simple,
                Medium     = request.IterationLimits.Medium,
                Large      = request.IterationLimits.Large,
                Enterprise = request.IterationLimits.Enterprise,
                Autonomous = request.IterationLimits.Autonomous
            },
            HistoryCompaction = new()
            {
                RecentMessages   = normalizedHistoryCompaction.RecentMessages,
                MaxSummaryEntries = normalizedHistoryCompaction.MaxSummaryEntries,
                MaxSummaryChars  = normalizedHistoryCompaction.MaxSummaryChars,
                MaxEntryChars    = normalizedHistoryCompaction.MaxEntryChars
            }
        };

        await settingsService.SaveOperationalSettingsAsync(settings);

        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiOperationalSettingsUpdated,
            Action = AuditActions.Updated,
            EntityType = AuditEntityTypes.AiSettings,
            Message = "AI operational settings were updated",
            Metadata = new
            {
                settings.MaxConversationHistoryMessages,
                settings.MaxIterations,
                settings.MaxConsecutiveFailures,
                settings.DefaultMaxOutputTokens,
                settings.AppVerificationDefaultStrategy,
                settings.AppVerificationAllowUserOverride,
                settings.AppExecutionDefaultPolicy,
                settings.AppExecutionAllowUserOverride,
                settings.IterationLimits.Simple,
                settings.IterationLimits.Medium,
                settings.IterationLimits.Large,
                settings.IterationLimits.Enterprise,
                settings.IterationLimits.Autonomous,
                settings.HistoryCompaction.RecentMessages,
                settings.HistoryCompaction.MaxSummaryEntries,
                settings.HistoryCompaction.MaxSummaryChars,
                settings.HistoryCompaction.MaxEntryChars
            }
        });

        return Ok(MapToResponse(settings));
    }

    private static AiProvidersOperationalConfigResponse MapToResponse(AiProvidersOperationalConfig s) => new()
    {
        MaxConversationHistoryMessages = s.MaxConversationHistoryMessages,
        MaxIterations                  = s.MaxIterations,
        MaxConsecutiveFailures         = s.MaxConsecutiveFailures,
        DefaultMaxOutputTokens         = s.DefaultMaxOutputTokens,
        AppVerificationDefaultStrategy = s.AppVerificationDefaultStrategy,
        AppVerificationAllowUserOverride = s.AppVerificationAllowUserOverride,
        AppExecutionDefaultPolicy = s.AppExecutionDefaultPolicy,
        AppExecutionAllowUserOverride = s.AppExecutionAllowUserOverride,
        IterationLimits = new()
        {
            Simple     = s.IterationLimits.Simple,
            Medium     = s.IterationLimits.Medium,
            Large      = s.IterationLimits.Large,
            Enterprise = s.IterationLimits.Enterprise,
            Autonomous = s.IterationLimits.Autonomous
        },
        HistoryCompaction = new()
        {
            RecentMessages    = s.HistoryCompaction.RecentMessages,
            MaxSummaryEntries = s.HistoryCompaction.MaxSummaryEntries,
            MaxSummaryChars   = s.HistoryCompaction.MaxSummaryChars,
            MaxEntryChars     = s.HistoryCompaction.MaxEntryChars
        }
    };

    private static HistoryCompactionConfig NormalizeHistoryCompaction(HistoryCompactionConfigRequest request)
    {
        return new HistoryCompactionConfig
        {
            RecentMessages = Math.Clamp(
                request.RecentMessages,
                HistoryCompactionBounds.MinRecentMessages,
                HistoryCompactionBounds.MaxRecentMessages),
            MaxSummaryEntries = Math.Clamp(
                request.MaxSummaryEntries,
                HistoryCompactionBounds.MinMaxSummaryEntries,
                HistoryCompactionBounds.MaxMaxSummaryEntries),
            MaxSummaryChars = Math.Clamp(
                request.MaxSummaryChars,
                HistoryCompactionBounds.MinMaxSummaryChars,
                HistoryCompactionBounds.MaxMaxSummaryChars),
            MaxEntryChars = Math.Clamp(
                request.MaxEntryChars,
                HistoryCompactionBounds.MinMaxEntryChars,
                HistoryCompactionBounds.MaxMaxEntryChars)
        };
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
    /// <summary>True when at least one active system (all-users) API key exists for this provider.</summary>
    public bool HasSystemKey { get; set; }
    /// <summary>True when at least one active model has IsDefault = true for this provider.</summary>
    public bool HasDefaultModel { get; set; }
    /// <summary>JSON merged into every request body for all models of this provider.</summary>
    public string? ExtraBodyJson { get; set; }
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
    public string? ExtraBodyJson { get; set; }
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
    public string? ExtraBodyJson { get; set; }
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
    public bool IsPlanningModel { get; set; }
    public int? MaxTokens { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsTools { get; set; }
    public bool SupportsVision { get; set; }
    public decimal? CostPer1MTokensInput { get; set; }
    public decimal? CostPer1MTokensOutput { get; set; }
    public string? ExtraBodyJson { get; set; }
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
    public bool IsPlanningModel { get; set; } = false;
    public int? MaxTokens { get; set; }
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsTools { get; set; } = false;
    public bool SupportsVision { get; set; } = false;
    public decimal? CostPer1MTokensInput { get; set; }
    public decimal? CostPer1MTokensOutput { get; set; }
    public string? ExtraBodyJson { get; set; }
    public int SortOrder { get; set; } = 0;
}

public class AdminUpdateAiModelRequest
{
    public string? ModelId { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsDefault { get; set; }
    public bool? IsPlanningModel { get; set; }
    public int? MaxTokens { get; set; }
    public bool? SupportsStreaming { get; set; }
    public bool? SupportsTools { get; set; }
    public bool? SupportsVision { get; set; }
    public decimal? CostPer1MTokensInput { get; set; }
    public decimal? CostPer1MTokensOutput { get; set; }
    public string? ExtraBodyJson { get; set; }
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
    /// <summary>True when the key was added by an admin and is available to all users.</summary>
    public bool IsSystemKey { get; set; }
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

public class GlobalAiDefaultsResponse
{
    public AiModelAdminResponse? DefaultModel { get; set; }
    public AiModelAdminResponse? PlanningModel { get; set; }
    public int DefaultMaxOutputTokens { get; set; }
}

public class IterationLimitsConfigResponse
{
    public int Simple     { get; set; }
    public int Medium     { get; set; }
    public int Large      { get; set; }
    public int Enterprise { get; set; }
    public int Autonomous { get; set; }
}

public class AiProvidersOperationalConfigResponse
{
    public int MaxConversationHistoryMessages { get; set; }
    public int MaxIterations                  { get; set; }
    public int MaxConsecutiveFailures         { get; set; }
    public int DefaultMaxOutputTokens         { get; set; }
    public string AppVerificationDefaultStrategy { get; set; } = AppVerificationStrategyNames.Balanced;
    public bool AppVerificationAllowUserOverride { get; set; } = true;
    public string AppExecutionDefaultPolicy { get; set; } = AppExecutionPolicyNames.Default;
    public bool AppExecutionAllowUserOverride { get; set; } = true;
    public IterationLimitsConfigResponse IterationLimits { get; set; } = new();
    public HistoryCompactionConfigResponse HistoryCompaction { get; set; } = new();
}

public class SaveAiSettingsRequest
{
    public int MaxConversationHistoryMessages { get; set; }
    public int MaxIterations                  { get; set; }
    public int MaxConsecutiveFailures         { get; set; }
    public int DefaultMaxOutputTokens         { get; set; }
    public string AppVerificationDefaultStrategy { get; set; } = AppVerificationStrategyNames.Balanced;
    public bool AppVerificationAllowUserOverride { get; set; } = true;
    public string AppExecutionDefaultPolicy { get; set; } = AppExecutionPolicyNames.Default;
    public bool AppExecutionAllowUserOverride { get; set; } = true;
    public IterationLimitsConfigResponse IterationLimits { get; set; } = new();
    [Required]
    public HistoryCompactionConfigRequest HistoryCompaction { get; set; } = new();
}

public class HistoryCompactionConfigRequest
{
    [Range(HistoryCompactionBounds.MinRecentMessages, HistoryCompactionBounds.MaxRecentMessages)]
    public int RecentMessages { get; set; }

    [Range(HistoryCompactionBounds.MinMaxSummaryEntries, HistoryCompactionBounds.MaxMaxSummaryEntries)]
    public int MaxSummaryEntries { get; set; }

    [Range(HistoryCompactionBounds.MinMaxSummaryChars, HistoryCompactionBounds.MaxMaxSummaryChars)]
    public int MaxSummaryChars { get; set; }

    [Range(HistoryCompactionBounds.MinMaxEntryChars, HistoryCompactionBounds.MaxMaxEntryChars)]
    public int MaxEntryChars { get; set; }
}

public class HistoryCompactionConfigResponse
{
    public int RecentMessages { get; set; }
    public int MaxSummaryEntries { get; set; }
    public int MaxSummaryChars { get; set; }
    public int MaxEntryChars { get; set; }
}

#endregion
