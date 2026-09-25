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
public class AdminAiModelsController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly AiApiKeyService aiApiKeyService;
    private readonly IAiProvidersSettingsService settingsService;
    private readonly IAuditEventService auditEventService;
    private readonly AdminAiProviderCreationService creationService;

    public AdminAiModelsController(
        ApplicationDbContext dbContext,
        AiApiKeyService aiApiKeyService,
        IAiProvidersSettingsService settingsService,
        IAuditEventService auditEventService,
        AdminAiProviderCreationService creationService)
    {
        this.dbContext = dbContext;
        this.aiApiKeyService = aiApiKeyService;
        this.settingsService = settingsService;
        this.auditEventService = auditEventService;
        this.creationService = creationService;
    }
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
        var result = await creationService.CreateModelAsync(providerId, request);
        if (result.ResourceNotFound)
            return NotFound("Provider not found");
        if (result.Error is not null)
            return BadRequest(result.Error);

        return CreatedAtAction(nameof(GetModels), new { providerId }, result.Value);
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

        var modelIdError = await AiProviderAdminUpdatePolicy.TrySetModelIdAsync(
            dbContext,
            model,
            id,
            request.ModelId);
        if (modelIdError is not null)
        {
            return BadRequest(modelIdError);
        }

        var defaultModelError = await AiProviderAdminUpdatePolicy.ValidateAndApplyDefaultModelAsync(
            dbContext,
            aiApiKeyService,
            model,
            request.IsDefault);
        if (defaultModelError is not null)
        {
            return BadRequest(defaultModelError);
        }

        await AiProviderAdminUpdatePolicy.ClearOtherPlanningModelsAsync(dbContext, model, request.IsPlanningModel);
        AiModelUpdateApplier.Apply(model, request);

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


}
