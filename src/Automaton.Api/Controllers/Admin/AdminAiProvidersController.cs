using Automaton.Mcp.SystemPrompts;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Constants;
using Automaton.Api.Services;
using Automaton.Api.Data;
using Automaton.Api.Config;
using Automaton.Api.Models;
using Automaton.Api.Entities;
namespace Automaton.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing AI providers and models.
/// </summary>
[ApiController]
[Route("api/admin/ai-providers")]
[Authorize(Policy = "Administrators")]
public class AdminAiProvidersController : ControllerBase
{
    private readonly ApplicationDbContext dbContext;
    private readonly IAuditEventService auditEventService;

    public AdminAiProvidersController(
        ApplicationDbContext dbContext,
        IAuditEventService auditEventService)
    {
        this.dbContext = dbContext;
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

        if (!await AiProviderAdminUpdatePolicy.TrySetNameAsync(dbContext, provider, id, request.Name))
        {
            return BadRequest($"A provider with name '{request.Name}' already exists.");
        }

        var previousIsActive = provider.IsActive;
        AiProviderUpdateApplier.Apply(provider, request);

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
    [JsonRequired]
    public int Simple     { get; set; }
    [JsonRequired]
    public int Medium     { get; set; }
    [JsonRequired]
    public int Large      { get; set; }
    [JsonRequired]
    public int Enterprise { get; set; }
    [JsonRequired]
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
    [JsonRequired]
    public int MaxConversationHistoryMessages { get; set; }
    [JsonRequired]
    public int MaxIterations                  { get; set; }
    [JsonRequired]
    public int MaxConsecutiveFailures         { get; set; }
    [JsonRequired]
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
