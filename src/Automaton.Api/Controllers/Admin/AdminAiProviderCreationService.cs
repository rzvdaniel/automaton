using Automaton.Api.Data;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers.Admin;

public sealed class AdminAiProviderCreationService(
    ApplicationDbContext dbContext,
    AiApiKeyService aiApiKeyService,
    IAuditEventService auditEventService,
    ILogger<AdminAiProviderCreationService> logger)
{
    public async Task<AdminAiResourceCreateResult<AiModelAdminResponse>> CreateModelAsync(
        Guid providerId,
        AdminCreateAiModelRequest request)
    {
        var provider = await dbContext.AiProviders.FindAsync(providerId);
        if (provider is null)
            return AdminAiResourceCreateResult<AiModelAdminResponse>.NotFound();

        var existingModel = await dbContext.AiModels
            .FirstOrDefaultAsync(model => model.AiProviderId == providerId && model.ModelId == request.ModelId);
        if (existingModel is not null)
            return AdminAiResourceCreateResult<AiModelAdminResponse>.Invalid(
                $"A model with ID '{request.ModelId}' already exists for this provider.");

        var validationError = await PrepareDefaultModelsAsync(provider.Name, request);
        if (validationError is not null)
            return AdminAiResourceCreateResult<AiModelAdminResponse>.Invalid(validationError);

        var model = CreateModel(providerId, request);
        dbContext.AiModels.Add(model);
        await dbContext.SaveChangesAsync();
        await WriteModelCreatedAuditAsync(provider, model);

        return AdminAiResourceCreateResult<AiModelAdminResponse>.Created(MapModelResponse(provider, model));
    }

    public async Task<AdminAiResourceCreateResult<AiApiKeyAdminResponse>> CreateProviderKeyAsync(
        Guid providerId,
        AdminCreateAiApiKeyRequest request)
    {
        var provider = await dbContext.AiProviders.FindAsync(providerId);
        if (provider is null)
            return AdminAiResourceCreateResult<AiApiKeyAdminResponse>.NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return AdminAiResourceCreateResult<AiApiKeyAdminResponse>.Invalid("Name is required");

        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return AdminAiResourceCreateResult<AiApiKeyAdminResponse>.Invalid("API key is required");

        var key = await aiApiKeyService.CreateAiApiKeyAsync(
            userId: Guid.Empty,
            name: request.Name,
            providerName: provider.Name,
            apiKey: request.ApiKey,
            description: request.Description);

        await PromotePreferredModelWhenFirstSystemKeyAsync(providerId, provider.Name);
        await WriteApiKeyCreatedAuditAsync(provider, key);

        return AdminAiResourceCreateResult<AiApiKeyAdminResponse>.Created(new AiApiKeyAdminResponse
        {
            Id = key.Id,
            Name = key.Name,
            ProviderName = key.ProviderName,
            Description = key.Description,
            CreatedAt = key.CreatedAt,
            LastUsedAt = key.LastUsedAt,
            IsActive = key.IsActive,
            IsSystemKey = key.IsSystemKey
        });
    }

    private async Task<string?> PrepareDefaultModelsAsync(string providerName, AdminCreateAiModelRequest request)
    {
        if (request.IsDefault)
        {
            var hasSystemKey = await aiApiKeyService.HasSystemApiKeyAsync(providerName);
            if (!hasSystemKey)
                return $"Cannot set '{providerName}' as the global default: no system API key exists for this provider. " +
                       "Add an API key with 'For all users' scope first.";

            var existingDefaults = await dbContext.AiModels.Where(model => model.IsDefault).ToListAsync();
            foreach (var model in existingDefaults)
                model.IsDefault = false;
        }

        if (request.IsPlanningModel)
        {
            var existingPlanners = await dbContext.AiModels.Where(model => model.IsPlanningModel).ToListAsync();
            foreach (var model in existingPlanners)
                model.IsPlanningModel = false;
        }

        return null;
    }

    private async Task PromotePreferredModelWhenFirstSystemKeyAsync(Guid providerId, string providerName)
    {
        var systemKeyCount = await dbContext.AiApiKeys.CountAsync(key => key.IsActive && key.CreatedByUserId == null);
        if (systemKeyCount != 1)
            return;

        var preferredModel = await dbContext.AiModels
            .Where(model => model.AiProviderId == providerId && model.IsActive)
            .OrderByDescending(model => model.SupportsTools)
            .ThenByDescending(model => model.IsDefault)
            .ThenBy(model => model.SortOrder)
            .FirstOrDefaultAsync();

        if (preferredModel is null || preferredModel.IsDefault)
            return;

        await dbContext.AiModels
            .Where(model => model.IsDefault)
            .ExecuteUpdateAsync(update => update.SetProperty(model => model.IsDefault, false));

        preferredModel.IsDefault = true;
        await dbContext.SaveChangesAsync();
        logger.LogDebug(
            "Auto-promoted model '{ModelId}' of provider '{ProviderName}' as global default (first system key)",
            preferredModel.ModelId,
            providerName);
    }

    private async Task WriteModelCreatedAuditAsync(AiProvider provider, AiModel model)
    {
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiModelCreated,
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.AiModel,
            EntityId = model.Id,
            Message = $"AI model '{model.ModelId}' was created for provider '{provider.Name}'",
            Metadata = new
            {
                ProviderId = provider.Id,
                ProviderName = provider.Name,
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
    }

    private async Task WriteApiKeyCreatedAuditAsync(AiProvider provider, AiApiKey key)
    {
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.AiApiKeyCreated,
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.AiApiKey,
            EntityId = key.Id,
            Message = $"AI API key '{key.Name}' was created for provider '{provider.Name}'",
            Metadata = new
            {
                key.Name,
                key.ProviderName,
                key.Description,
                key.IsActive,
                key.IsSystemKey
            }
        });
    }

    private static AiModel CreateModel(Guid providerId, AdminCreateAiModelRequest request) => new()
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

    private static AiModelAdminResponse MapModelResponse(AiProvider provider, AiModel model) => new()
    {
        Id = model.Id,
        AiProviderId = model.AiProviderId,
        ProviderName = provider.Name,
        ModelId = model.ModelId,
        DisplayName = model.DisplayName,
        Description = model.Description,
        IsActive = model.IsActive,
        IsDefault = model.IsDefault,
        IsPlanningModel = model.IsPlanningModel,
        MaxTokens = model.MaxTokens,
        SupportsStreaming = model.SupportsStreaming,
        SupportsTools = model.SupportsTools,
        SupportsVision = model.SupportsVision,
        CostPer1MTokensInput = model.CostPer1MTokensInput,
        CostPer1MTokensOutput = model.CostPer1MTokensOutput,
        ExtraBodyJson = model.ExtraBodyJson,
        SortOrder = model.SortOrder,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt
    };
}

public sealed record AdminAiResourceCreateResult<T>(T? Value, bool ResourceNotFound, string? Error)
{
    public static AdminAiResourceCreateResult<T> Created(T value) => new(value, false, null);
    public static AdminAiResourceCreateResult<T> NotFound() => new(default, true, null);
    public static AdminAiResourceCreateResult<T> Invalid(string error) => new(default, false, error);
}
