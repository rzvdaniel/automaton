using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers.Admin;

internal static class AiProviderAdminUpdatePolicy
{
    public static async Task<bool> TrySetNameAsync(
        ApplicationDbContext dbContext,
        AiProvider provider,
        Guid providerId,
        string? requestedName)
    {
        if (requestedName is null || requestedName == provider.Name)
        {
            return true;
        }

        var duplicate = await dbContext.AiProviders
            .AnyAsync(candidate => candidate.Name == requestedName && candidate.Id != providerId);
        if (duplicate)
        {
            return false;
        }

        provider.Name = requestedName;
        return true;
    }

    public static async Task<string?> TrySetModelIdAsync(
        ApplicationDbContext dbContext,
        AiModel model,
        Guid modelId,
        string? requestedModelId)
    {
        if (requestedModelId is null || requestedModelId == model.ModelId)
        {
            return null;
        }

        var duplicate = await dbContext.AiModels.AnyAsync(candidate =>
            candidate.AiProviderId == model.AiProviderId
            && candidate.ModelId == requestedModelId
            && candidate.Id != modelId);
        if (duplicate)
        {
            return $"A model with ID '{requestedModelId}' already exists for this provider.";
        }

        model.ModelId = requestedModelId;
        return null;
    }

    public static async Task<string?> ValidateAndApplyDefaultModelAsync(
        ApplicationDbContext dbContext,
        AiApiKeyService aiApiKeyService,
        AiModel model,
        bool? requestedDefault)
    {
        if (requestedDefault != true || model.IsDefault)
        {
            return null;
        }

        var provider = await dbContext.AiProviders.FindAsync(model.AiProviderId);
        var providerName = provider?.Name ?? string.Empty;
        if (!await aiApiKeyService.HasSystemApiKeyAsync(providerName))
        {
            return $"Cannot set '{providerName}' as the global default: no system API key exists for this provider. " +
                   "Add an API key with 'For all users' scope first.";
        }

        var existingDefaults = await dbContext.AiModels
            .Where(candidate => candidate.IsDefault && candidate.Id != model.Id)
            .ToListAsync();
        foreach (var existingDefault in existingDefaults)
        {
            existingDefault.IsDefault = false;
        }

        return null;
    }

    public static async Task ClearOtherPlanningModelsAsync(
        ApplicationDbContext dbContext,
        AiModel model,
        bool? requestedPlanningModel)
    {
        if (requestedPlanningModel != true || model.IsPlanningModel)
        {
            return;
        }

        var existingPlanners = await dbContext.AiModels
            .Where(candidate => candidate.IsPlanningModel && candidate.Id != model.Id)
            .ToListAsync();
        foreach (var planner in existingPlanners)
        {
            planner.IsPlanningModel = false;
        }
    }
}

internal static class AiProviderUpdateApplier
{
    public static void Apply(AiProvider provider, AdminUpdateAiProviderRequest request)
    {
        if (request.DisplayName is not null) provider.DisplayName = request.DisplayName;
        if (request.Description is not null) provider.Description = request.Description;
        if (request.BaseUrl is not null) provider.BaseUrl = request.BaseUrl;
        if (request.IsActive.HasValue) provider.IsActive = request.IsActive.Value;
        if (request.RequiresApiKey.HasValue) provider.RequiresApiKey = request.RequiresApiKey.Value;
        if (request.EnvironmentKeyName is not null) provider.EnvironmentKeyName = request.EnvironmentKeyName;
        if (request.ApiKeyHelpUrl is not null) provider.ApiKeyHelpUrl = request.ApiKeyHelpUrl;
        if (request.IconName is not null) provider.IconName = request.IconName;
        if (request.SortOrder.HasValue) provider.SortOrder = request.SortOrder.Value;
        if (request.ExtraBodyJson is not null)
        {
            provider.ExtraBodyJson = string.IsNullOrWhiteSpace(request.ExtraBodyJson) ? null : request.ExtraBodyJson;
        }

        provider.UpdatedAt = DateTime.UtcNow;
    }
}

internal static class AiModelUpdateApplier
{
    public static void Apply(AiModel model, AdminUpdateAiModelRequest request)
    {
        ApplyDisplaySettings(model, request);
        ApplyModelPolicy(model, request);
        ApplyCapabilities(model, request);
        ApplyUsageLimits(model, request);
        ApplyExtraBody(model, request.ExtraBodyJson);
        model.UpdatedAt = DateTime.UtcNow;
    }

    private static void ApplyDisplaySettings(AiModel model, AdminUpdateAiModelRequest request)
    {
        if (request.DisplayName is not null) model.DisplayName = request.DisplayName;
        if (request.Description is not null) model.Description = request.Description;
        if (request.SortOrder.HasValue) model.SortOrder = request.SortOrder.Value;
    }

    private static void ApplyModelPolicy(AiModel model, AdminUpdateAiModelRequest request)
    {
        if (request.IsActive.HasValue) model.IsActive = request.IsActive.Value;
        if (request.IsDefault.HasValue) model.IsDefault = request.IsDefault.Value;
        if (request.IsPlanningModel.HasValue) model.IsPlanningModel = request.IsPlanningModel.Value;
    }

    private static void ApplyCapabilities(AiModel model, AdminUpdateAiModelRequest request)
    {
        if (request.SupportsStreaming.HasValue) model.SupportsStreaming = request.SupportsStreaming.Value;
        if (request.SupportsTools.HasValue) model.SupportsTools = request.SupportsTools.Value;
        if (request.SupportsVision.HasValue) model.SupportsVision = request.SupportsVision.Value;
    }

    private static void ApplyUsageLimits(AiModel model, AdminUpdateAiModelRequest request)
    {
        if (request.MaxTokens.HasValue) model.MaxTokens = request.MaxTokens.Value;
        if (request.CostPer1MTokensInput.HasValue) model.CostPer1MTokensInput = request.CostPer1MTokensInput.Value;
        if (request.CostPer1MTokensOutput.HasValue) model.CostPer1MTokensOutput = request.CostPer1MTokensOutput.Value;
    }

    private static void ApplyExtraBody(AiModel model, string? extraBodyJson)
    {
        if (extraBodyJson is not null)
        {
            model.ExtraBodyJson = string.IsNullOrWhiteSpace(extraBodyJson) ? null : extraBodyJson;
        }
    }
}
